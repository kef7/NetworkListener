﻿namespace NetworkListener
{
    using global::NetworkListener.NetworkCommunicationProcessors;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    /// <summary>
    /// Network listener to listen and respond to client connections
    /// </summary>
    public class NetworkListener : IDisposable
    {
        /// <summary>
        /// Client thread monitor run delay
        /// </summary>
        private const int MONITOR_DELAY = 5000;

        /// <summary>
        /// Thread locking object ref
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// Client message received event signature
        /// </summary>
        public event EventHandler<NetworkListenerReceiveEventArgs>? ClientMessageReceived = null;

        /// <summary>
        /// Backing field to property <see cref="MaxClientConnections"/>
        /// </summary>
        private int _maxClientConnections = 7300;

        /// <summary>
        /// Cancellation token source so we can attempt to have some control
        /// </summary>
        private CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Client connection threads
        /// </summary>
        private ICollection<Thread> _clientThreads = new List<Thread>();

        /// <summary>
        /// Clients monitor thread
        /// </summary>
        private Thread? _monitorThread = null;

        /// <summary>
        /// Logger; mainly for trace logging
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// Count of the number of client threads
        /// </summary>
        public int ClientThreadCount
        {
            get
            {
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        return _clientThreads.Count;
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }

                lock (_lock)
                {
                    return _clientThreads.Count;
                }
            }
        }

        /// <summary>
        /// The listener socket
        /// </summary>
        protected Socket? ListenerSocket { get; set; }

        /// <summary>
        /// The IP address used on the listener
        /// </summary>
        public IPAddress IPAddress { get; internal set; } = IPAddress.Any;

        /// <summary>
        /// The port number to listen on
        /// </summary>
        public int Port { get; internal set; } = -1;

        /// <summary>
        /// The socket type used on the listener
        /// </summary>
        public SocketType SocketType { get; internal set; } = SocketType.Stream;

        /// <summary>
        /// The protocol type used on the listener
        /// </summary>
        public ProtocolType ProtocolType { get; internal set; } = ProtocolType.Tcp;

        /// <summary>
        /// The network communication processor object to process each network
        /// </summary>
        public INetworkCommunicationProcessor NetworkCommunicationProcessor { get; internal set; } = null!;

        /// <summary>
        /// Handle parallel connections
        /// </summary>
        public bool HandleParallelConnections { get; internal set; } = true;

        /// <summary>
        /// Max client connections
        /// </summary>
        public int MaxClientConnections
        {
            get
            {
                return _maxClientConnections;
            }
            internal set
            {
                if (value < 1)
                {
                    _maxClientConnections = 1;
                    return;
                }

                if (value > 7300)
                {
                    _maxClientConnections = 7300;
                    return;
                }

                _maxClientConnections = value;
            }
        }

        /// <summary>
        /// The cancellation token tracked in operations for cancellation purposes
        /// </summary>
        public CancellationToken CancellationToken { get; protected set; }

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="logger">Required logger for trace logging</param>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        internal NetworkListener(ILogger<NetworkListener> logger, int? port = null, int? maxClientConnections = null, INetworkCommunicationProcessor? networkCommunicationProcessor = null)
        {
            Logger = logger;

            if (port.HasValue)
            {
                var p = port.Value;

                // Validate port lower range
                if (p < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(port));
                }

                // Validate port upper range
                if (p > 65535)
                {
                    throw new ArgumentOutOfRangeException(nameof(port));
                }

                Port = p;
            }

            if (maxClientConnections.HasValue)
            {
                var mcc = maxClientConnections.Value;

                // Adjust lower range
                if (mcc < 1)
                {
                    mcc = 1;
                }

                MaxClientConnections = mcc;
            }

            if (networkCommunicationProcessor != null)
            {
                if (networkCommunicationProcessor.MaxBufferSize < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(networkCommunicationProcessor.MaxBufferSize));
                }

                NetworkCommunicationProcessor = networkCommunicationProcessor;
            }
        }

        /// <summary>
        /// Start listening, accepting connections, and receiving messages.
        /// </summary>
        /// <remarks>
        /// This will lock the thread it is on
        /// </remarks>
        /// <returns></returns>
        public async Task Listen(CancellationToken? cancellationToken = null)
        {
            // Cancellation token set
            bool usedAnotherCancellationToken = false;
            if (cancellationToken != null)
            {
                CancellationToken = cancellationToken.Value;
                usedAnotherCancellationToken = true;
            }

            try
            {
                // Init and configure listener socket
                try
                {
                    // Stop current listener if needed
                    if (ListenerSocket != null)
                    {
                        Logger.LogTrace("Tearing down old listener.");

                        ListenerSocket.Shutdown(SocketShutdown.Both);
                        ListenerSocket.Close();
                        ListenerSocket.Dispose();
                        ListenerSocket = null;
                    }

                    Logger.LogTrace("Building new listener.");

                    // Build IP end point
                    var ipEndPoint = new IPEndPoint(IPAddress, Port);

                    // New-up listener
                    ListenerSocket = new Socket(IPAddress.AddressFamily, SocketType, ProtocolType);

                    // Configure listener
                    ListenerSocket.Bind(ipEndPoint);

                    // Start listening
                    ListenerSocket.Listen(Port);

                    Logger.LogInformation("Listening on end point {EndPoint}", ipEndPoint);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in listener setup.");
                    return;
                }

                // Loop and accept connections and read/write on network
                uint clientCntr = 0;
                while (true)
                {
                    // Check for cancellation
                    if (CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // Accept and process connections
                    try
                    {
                        // Check to see if we should handle multiple connections
                        if (!HandleParallelConnections &&
                            ClientThreadCount > 0)
                        {
                            Logger.LogTrace("Handling multiple connections disabled. Waiting current connection to close...");

                            // Spin wait if having client and should not handle multiple
                            await WaitForClientProcessing(1);

                            Logger.LogTrace("Connection closed; accepting another connection.");
                        }

                        // Is client count maxed
                        if (HandleParallelConnections &&
                            ClientThreadCount >= MaxClientConnections)
                        {
                            Logger.LogWarning("Max client connections reached. {MaxClientConnections}", MaxClientConnections);

                            // Spin wait if having clients and should not handle more
                            await WaitForClientProcessing(MaxClientConnections);
                        }

                        Logger.LogTrace("Waiting to accept client connections.");

                        // Accept client connection
                        var socket = await ListenerSocket.AcceptAsync(CancellationToken);

                        // Check for cancellation
                        if (CancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        // Process accepted client connection on a new thread
                        if (socket != null)
                        {
                            // Name client
                            var clientName = $"Client-{clientCntr++}";

                            Logger.LogTrace("Remote client connected from {RemoteEndPoint} and named {ClientName}", socket.RemoteEndPoint, clientName);

                            // Create new client thread
                            var thread = new Thread(() =>
                            {
                                // Process the client connection and wait.
                                try
                                {
                                    // Must wait here so thread will not die.
                                    // Do not call ProcessConnection() with await as this will
                                    // trigger a async thread and the main thread here will leave
                                    // execution causing count to drop and monitor thread to
                                    // remove from list.
                                    Task.WaitAll(new Task[] { ProcessConnection(socket) }, CancellationToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    Logger.LogInformation("Client processing thread canceled.");
                                }
                                finally
                                {
                                    // Dispose the client connection
                                    DisposeClient(socket);
                                }
                            });

                            // Configure thread
                            thread.IsBackground = true;
                            thread.Name = clientName;
                            thread.Priority = ThreadPriority.Normal;

                            // Add to list of client threads
                            if (Monitor.TryEnter(_lock))
                            {
                                try
                                {
                                    _clientThreads.Add(thread);
                                }
                                finally
                                {
                                    Monitor.Exit(_lock);
                                }
                            }
                            else
                            {
                                lock (_lock)
                                {
                                    _clientThreads.Add(thread);
                                }
                            }

                            // Start thread
                            thread.Start();

                            // Kick off thread monitor
                            StartClientThreadMonitoring();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogInformation("Listener processing canceled.");
                    }
                    catch (Exception loopEx)
                    {
                        Logger.LogError(loopEx, "Error in listener processing.");
                    }

                    // Reset client counter
                    if (clientCntr == uint.MaxValue)
                    {
                        clientCntr = 0;
                    }
                } // END - while loop processing
            }
            finally
            {
                // Set our cancellation token back
                if (usedAnotherCancellationToken)
                {
                    CancellationToken = _cts.Token;
                }

                // TODO: Clean up client threads (close connections and stop threads)

                Logger.LogTrace("Exit listener.");
            }
        }

        /// <summary>
        /// Wait for clients to process
        /// </summary>
        /// <returns></returns>
        private async Task WaitForClientProcessing(int max)
        {
            // Fix max
            if (max < 1)
            {
                max = 1;
            }

            // Spin wait if having client and should not handle multiple
            await Task.Run(() =>
            {
                while (ClientThreadCount >= max)
                {
                    Task.Delay(1000, CancellationToken);

                    // Check for cancellation
                    if (CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            });
        }

        /// <summary>
        /// Process accepted client socket connection which is done in <see cref="Listen(CancellationToken?)"/>
        /// </summary>
        /// <param name="clientSocket">The accepted client socket</param>
        /// <returns></returns>
        private async Task ProcessConnection(Socket clientSocket)
        {
            if (clientSocket is null)
            {
                throw new ArgumentNullException(nameof(clientSocket));
            }

            // Get or set client name
            var clientName = Thread.CurrentThread.Name ?? Guid.NewGuid().ToString();

            Logger.LogTrace("{ClientName} - Processing connection from {RemoteEndPoint}", clientName, clientSocket.RemoteEndPoint);

            // Declare vars and kick off loop to process client
            uint loopCntr = 0;
            int msgCntr = 0;
            while (true)
            {
                // Check if data available from client
                if (clientSocket.Available > 0)
                {
                    // Increment message counter
                    msgCntr += 1;

                    try
                    {
                        // Declare buffer size
                        var maxBufferSize = clientSocket.Available;
                        if (maxBufferSize > NetworkCommunicationProcessor.MaxBufferSize)
                        {
                            maxBufferSize = NetworkCommunicationProcessor.MaxBufferSize;
                        }

                        // Declare buffer
                        var buffer = new byte[maxBufferSize];

                        // Receive message from client
                        var received = await clientSocket.ReceiveAsync(buffer, SocketFlags.None, CancellationToken);

                        // Get encoded message
                        var message = NetworkCommunicationProcessor.Encode(buffer);

                        Logger.LogTrace("{ClientName} - Message from {RemoteEndPoint} received. Message: {Message}", clientName, clientSocket.RemoteEndPoint, message);

                        // Process message
                        var ts = DateTime.UtcNow;
                        var remoteEndPoint = clientSocket.RemoteEndPoint;
                        var ackMessage = NetworkCommunicationProcessor.ProcessCommunication(message, ts, remoteEndPoint);

                        // Trigger message received event if needed
                        ClientMessageReceived?.Invoke(this, new NetworkListenerReceiveEventArgs
                        {
                            Message = message,
                            Timestamp = ts,
                            RemoteEndPoint = remoteEndPoint
                        });

                        // Trigger message acknowledgment
                        if (ackMessage != null)
                        {
                            Logger.LogTrace("{ClientName} - Sending ACK message to {RemoteEndPoint}. ACK message: {AckMessage}", clientName, remoteEndPoint, ackMessage);

                            // Get decoded message
                            var ackMessageBytes = NetworkCommunicationProcessor.Decode(ackMessage);

                            // Send acknowledgment message to client
                            _ = await clientSocket.SendAsync(ackMessageBytes, SocketFlags.None, CancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogInformation("{ClientName} - Client processing canceled.", clientName);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "{ClientName} - Error in processing client connection.", clientName);
                    }
                } // End - if available

                // Disconnect if needed
                if (!clientSocket.IsConnected())
                {
                    Logger.LogInformation("{ClientName} - Client disconnected.", clientName);
                    break;
                }

                // Wait a bit
                Thread.Sleep(300);

                // Disconnect if needed
                if (!clientSocket.IsConnected())
                {
                    Logger.LogInformation("{ClientName} - Client disconnected.", clientName);
                    break;
                }

                // Write heartbeat message
                if ((loopCntr % 10) == 0 || loopCntr == 0)
                {
                    Logger.LogTrace("{ClientName} - Still processing; currently on [{LoopCounter}] iteration.", clientName, loopCntr);
                }

                // Increment loop counter; reset if needed
                loopCntr += 1;
                if (loopCntr >= (uint.MaxValue - 1))
                {
                    loopCntr = 0;
                }
            } // End - while true

            Logger.LogTrace("{ClientName} - Leaving client connection processing", clientName);
        }

        /// <summary>
        /// Start client thread monitoring
        /// </summary>
        private void StartClientThreadMonitoring()
        {
            // Create monitor thread
            if (_monitorThread == null ||
                _monitorThread.ThreadState == ThreadState.Stopped)
            {
                _monitorThread = BuildMonitorThread();
            }

            // Start monitor thread if needed
            if (_monitorThread.ThreadState == ThreadState.Unstarted)
            {
                Logger.LogTrace("Starting clients thread monitor.");
                _monitorThread.Start();
            }
        }

        /// <summary>
        /// Build monitor thread
        /// </summary>
        /// <returns></returns>
        private Thread BuildMonitorThread()
        {
            // New-up monitor thread
            _monitorThread = new Thread(() =>
            {
                try
                {
                    Task.WaitAll(new Task[] { MonitorClientThreads() }, CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Logger.LogTrace("Monitor thread canceled.");
                }
                catch (ThreadInterruptedException)
                {
                    Logger.LogTrace("Monitor thread interrupted during wait.");
                }
            });

            _monitorThread.Name = "Clients Monitor Thread";
            _monitorThread.Priority = ThreadPriority.BelowNormal;
            _monitorThread.IsBackground = false;

            return _monitorThread;
        }

        /// <summary>
        /// Monitor client threads
        /// </summary>
        private async Task MonitorClientThreads()
        {
            Logger.LogTrace("Monitoring clients.");

            try
            {
                // Delay monitor execution
                await Task.Delay(MONITOR_DELAY, CancellationToken);

                // Check for client threads
                var continueToRun = false;
                do
                {
                    // Clean up stopped client threads
                    CleanUpStoppedClientThreads();

                    // Check to see if we need to continue to monitor
                    if (Monitor.TryEnter(_lock))
                    {
                        try
                        {
                            continueToRun = _clientThreads.Count > 0;
                        }
                        finally
                        {
                            Monitor.Exit(_lock);
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Could not get lock on object to see if we need to continue to monitor client threads.");
                        continueToRun = true;
                    }

                    // Delay monitor execution if we need to continue
                    if (continueToRun)
                    {
                        await Task.Delay(MONITOR_DELAY, CancellationToken);
                    }

                    // Break
                    if (CancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                while (continueToRun);

            }
            catch (TaskCanceledException)
            {
                Logger.LogTrace("Monitor delay canceled.");
            }
            finally
            {
                Logger.LogTrace("Leaving client monitor.");
            }
        }

        /// <summary>
        /// Clean up client threads that have stopped
        /// </summary>
        private void CleanUpStoppedClientThreads()
        {
            if (_clientThreads.Count > 0)
            {
                // New-up threads to remove container
                var removeThese = new List<Thread>();

                // Lock it up to work on removal
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        Logger.LogTrace("Checking for stopped client threads.");

                        // Get threads to remove
                        for (var i = 0; i < _clientThreads.Count; i++)
                        {
                            var thread = _clientThreads.ElementAt(i);
                            if (thread.ThreadState == ThreadState.Stopped)
                            // Shows thread is stopped but logs from processing still going on from within thread... ?
                            {
                                removeThese.Add(thread);
                            }
                        }

                        // Remove threads from container
                        if (removeThese.Count > 0)
                        {
                            Logger.LogTrace("Removing client threads.");

                            // Remove threads
                            foreach (var thread in removeThese)
                            {
                                _clientThreads.Remove(thread);
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
                else
                {
                    Logger.LogWarning("Could not get lock on object to clean up client threads.");
                }

                // Call garbage collector
                if (removeThese.Count > 0)
                {
                    Logger.LogTrace("Force GC.");
                    GC.Collect();
                }
            }
        }

        /// <summary>
        /// Dispose client socket
        /// </summary>
        /// <param name="clientSocket">The client socket to dispose off</param>
        private void DisposeClient(Socket? clientSocket)
        {
            if (clientSocket != null)
            {
                Logger.LogTrace("Disposing client socket for remote endpoint {RemoteEndpoint}", clientSocket.RemoteEndPoint);
                clientSocket.Shutdown(SocketShutdown.Both);
                clientSocket.Close();
                clientSocket.Dispose();
                clientSocket = null;
            }
        }

        /// <summary>
        /// Dispose our resources
        /// </summary>
        public void Dispose()
        {
            if (_monitorThread != null)
            {
                _monitorThread.Interrupt();
            }

            // Cancel
            _cts.Cancel();

            if (ListenerSocket != null)
            {
                ListenerSocket.Shutdown(SocketShutdown.Both);
                ListenerSocket.Close();
                ListenerSocket.Dispose();
                ListenerSocket = null;
            }

            if (_clientThreads.Count > 0)
            {
                // Clean up stopped client threads
                CleanUpStoppedClientThreads();
            }
        }
    }
}
