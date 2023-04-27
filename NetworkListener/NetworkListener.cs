namespace NetworkListener
{
    using global::NetworkListener.NetworkClientDataProcessors;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using System;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;

    /// <summary>
    /// Network listener to listen and respond to client connections
    /// </summary>
    public class NetworkListener : IDisposable
    {
        /// <summary>
        /// Client thread meta-data
        /// </summary>
        internal struct ClientThreadMeta
        {
            /// <summary>
            /// Client thread name
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Client thread
            /// </summary>
            public Thread Thread { get; set; }

            /// <summary>
            /// Cancellation token for client thread cancellation
            /// </summary>
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }

        /// <summary>
        /// Client thread monitor run delay
        /// </summary>
        private const int MONITOR_DELAY = 5000;

        /// <summary>
        /// Thread locking object ref
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// Listener started event signature
        /// </summary>
        public event EventHandler<ListenerEventArgs>? Started = null;

        /// <summary>
        /// Listener stopped event signature
        /// </summary>
        public event EventHandler<ListenerEventArgs>? Stopped = null;

        /// <summary>
        /// Client connected event signature
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected = null;

        /// <summary>
        /// Client disconnected event signature
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected = null;

        /// <summary>
        /// Client data received event signature
        /// </summary>
        public event EventHandler<ClientDataReceivedEventArgs>? ClientDataReceived = null;

        /// <summary>
        /// Client waiting for data event signature
        /// </summary>
        public event EventHandler<ClientWaitingEventArgs>? ClientWaiting = null;

        /// <summary>
        /// Client error event signature
        /// </summary>
        public event EventHandler<ClientErrorEventArgs>? ClientError = null;

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
        private ICollection<ClientThreadMeta> _clientThreads = new List<ClientThreadMeta>();

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
        protected Socket? ListenerSocket { get; set; } = null;

        /// <summary>
        /// Server host name
        /// </summary>
        public string? HostName { get; set; } = null;

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
        /// The certificate to use for SSL/TLS communications
        /// </summary>
        public X509Certificate? Certificate { get; internal set; } = null;

        /// <summary>
        /// The secure protocol types to use for secured communications
        /// </summary>
        public SslProtocols? SslProtocols { get; internal set; } = null;

        /// <summary>
        /// The network client data processor object to process each network
        /// </summary>
        public INetworkClientDataProcessor NetworkClientDataProcessor { get; internal set; } = null!;

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
        /// Network listener constructor
        /// </summary>
        /// <param name="logger">Required logger for trace logging</param>
        /// <param name="port">Port to listen on</param>
        /// <param name="maxClientConnections">Max number of client connection</param>
        /// <param name="networkClientDataProcessor">The network client data processor to process client data</param>
        internal NetworkListener(ILogger<NetworkListener> logger, int? port = null, int? maxClientConnections = null, INetworkClientDataProcessor? networkClientDataProcessor = null)
        {
            Logger = logger ?? NullLoggerFactory.Instance.CreateLogger<NetworkListener>();

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

            if (networkClientDataProcessor != null)
            {
                if (networkClientDataProcessor.MaxBufferSize < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(networkClientDataProcessor.MaxBufferSize));
                }

                NetworkClientDataProcessor = networkClientDataProcessor;
            }
        }

        /// <summary>
        /// Start listening, accepting connections, and receiving data
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

            // Vars
            IPEndPoint? ipEndPoint = null;

            try
            {
                // Init and configure listener socket
                try
                {
                    // Stop current listener if needed
                    if (ListenerSocket != null)
                    {
                        Logger.LogDebug("Tearing down old listener");

                        ListenerSocket.Shutdown(SocketShutdown.Both);
                        ListenerSocket.Close();
                        ListenerSocket.Dispose();
                        ListenerSocket = null;
                    }

                    Logger.LogDebug("Building new listener");

                    // Build IP end point
                    ipEndPoint = new IPEndPoint(IPAddress, Port);

                    // New-up listener
                    ListenerSocket = new Socket(IPAddress.AddressFamily, SocketType, ProtocolType);

                    // Configure listener
                    ListenerSocket.Bind(ipEndPoint);

                    // Start listening
                    ListenerSocket.Listen(Port);

                    Logger.LogInformation("Listening on end point {EndPoint}", ipEndPoint);
                    Logger.LogDebug("Using data processor type {NcdpType}", NetworkClientDataProcessor.GetType().FullName);

                    // Trigger started event
                    Started?.Invoke(this, new ListenerEventArgs
                    {
                        LocalEndPoint = ipEndPoint,
                        HostName = HostName,
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in listener configuration or startup");
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
                            Logger.LogWarning("Handling multiple connections is disabled. Waiting for current connection to close...");

                            // Spin wait if having client and should not handle multiple
                            await WaitForClientProcessing(1);
                        }

                        // Is client count maxed
                        if (HandleParallelConnections &&
                            ClientThreadCount >= MaxClientConnections)
                        {
                            Logger.LogWarning("Max client connections reached; max connections [{MaxClientConnections}]", MaxClientConnections);

                            // Spin wait if having clients and should not handle more
                            await WaitForClientProcessing(MaxClientConnections);
                        }

                        Logger.LogInformation("Waiting to accept client connections");

                        // Accept client connection
                        var socket = await ListenerSocket.AcceptAsync(CancellationToken);

                        // Trigger client connected event
                        ClientConnected?.Invoke(this, new ClientConnectedEventArgs
                        {
                            RemoteEndPoint = socket.RemoteEndPoint,
                            Timestamp = DateTime.UtcNow
                        });

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

                            // Client cancellation token source
                            var cts = new CancellationTokenSource();

                            Logger.LogInformation("Remote client connected from {RemoteEndPoint} and given name {ClientName}", socket.RemoteEndPoint, clientName);

                            // Create new client thread
                            var thread = new Thread(() =>
                            {
                                // Process the client connection and wait.
                                var triggeredOnClientDisconnected = false;
                                try
                                {
                                    // Must wait here so thread will not die.
                                    // Do not call ProcessConnection() with await as this will
                                    // trigger a async thread and the main thread here will leave
                                    // execution causing count to drop and monitor thread to
                                    // remove from list.
                                    Task.WaitAll(new Task[] { ProcessClientConnection(socket, cts.Token) }, cts.Token);
                                }
                                catch (AggregateException aggEx)
                                {
                                    // Get base exception
                                    var ex = aggEx.GetBaseException();

                                    // Trigger client error event
                                    ClientError?.Invoke(this, new ClientErrorEventArgs
                                    {
                                        Exception = ex,
                                        RemoteEndPoint = socket.RemoteEndPoint,
                                        Timestamp = DateTime.UtcNow
                                    });

                                    // Check if canceled
                                    if (ex is OperationCanceledException)
                                    {
                                        Logger.LogWarning("{ClientName} - Client processing thread canceled", clientName);

                                        // Trigger client disconnected event
                                        OnClientDisconnected(socket, ex as OperationCanceledException);
                                        triggeredOnClientDisconnected = true;
                                    }
                                    // Check if disconnected abruptly
                                    else if (ex is SocketException sEx && sEx.NativeErrorCode == 10054)
                                    {
                                        Logger.LogError("{ClientName} - Client disconnected abruptly", clientName);

                                        // Trigger client disconnected event
                                        OnClientDisconnected(socket, sEx);
                                        triggeredOnClientDisconnected = true;
                                    }
                                    else
                                    {
                                        Logger.LogError(ex, "{ClientName} - Error in client processing", clientName);
                                    }
                                }
                                finally
                                {
                                    // Disconnect
                                    try
                                    {
                                        socket.Disconnect(false);
                                    }
                                    finally
                                    {
                                        // Should we trigger client disconnected event
                                        if (!triggeredOnClientDisconnected)
                                        {
                                            // Trigger client disconnected event
                                            OnClientDisconnected(socket);
                                        }
                                    }

                                    // Dispose the client connection
                                    DisposeClient(socket);
                                }
                            });

                            // Configure thread
                            thread.IsBackground = true;
                            thread.Name = clientName;
                            thread.Priority = ThreadPriority.Normal;

                            // Build client thread meta
                            var ctMeta = new ClientThreadMeta
                            {
                                Name = clientName,
                                Thread = thread,
                                CancellationTokenSource = cts
                            };

                            // Add meta to list of client threads
                            lock (_lock)
                            {
                                _clientThreads.Add(ctMeta);
                            }

                            // Start thread
                            thread.Start();

                            // Kick off thread monitor
                            StartClientThreadMonitoring();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogWarning("Listener processing canceled");
                    }
                    catch (Exception loopEx)
                    {
                        Logger.LogError(loopEx, "Error in listener processing");
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

                Logger.LogTrace("Leaving listener");

                // Trigger stop event
                Stopped?.Invoke(this, new ListenerEventArgs
                {
                    LocalEndPoint = ipEndPoint,
                    HostName = HostName,
                    Timestamp = DateTime.UtcNow
                });
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
        /// <param name="cancellationToken">A cancellation token to cancel the client socket processing</param>
        /// <returns></returns>
        private async Task ProcessClientConnection(Socket clientSocket, CancellationToken cancellationToken)
        {
            if (clientSocket is null)
            {
                throw new ArgumentNullException(nameof(clientSocket));
            }

            // Get or set client name
            var clientName = Thread.CurrentThread.Name ?? Guid.NewGuid().ToString();

            Logger.LogInformation("{ClientName} - Processing connection from {RemoteEndPoint}", clientName, clientSocket.RemoteEndPoint);

            try
            {
                // Get client stream
                using var clientStream = GetClientStream(clientSocket, Certificate);

                // Declare vars and kick off loop to process client
                uint loopCntr = 0;
                int dataCntr = 0;
                while (true)
                {
                    // Check if data available from client
                    if (clientSocket!.Available > 0)
                    {
                        // Increment data counter
                        dataCntr += 1;

                        try
                        {
                            // Declare buffer size
                            var maxBufferSize = clientSocket.Available;
                            if (maxBufferSize > NetworkClientDataProcessor.MaxBufferSize)
                            {
                                maxBufferSize = NetworkClientDataProcessor.MaxBufferSize;
                            }

                            // Declare buffer
                            var buffer = new byte[maxBufferSize];

                            // Read all data from client
                            var received = -1;
                            var iteration = 1;
                            do
                            {
                                Logger.LogInformation("{ClientName} - Receiving data from {RemoteEndPoint}; iteration [{Iteration}]", clientName, clientSocket.RemoteEndPoint, iteration);

                                // Receive data from client
                                received = await clientStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                                Logger.LogDebug("{ClientName} - Received [{BytesReceived}] bytes from {RemoteEndPoint}; iteration [{Iteration}]", clientName, received, clientSocket.RemoteEndPoint, iteration);

                                // Check cancellation
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                                    break;
                                }

                                // Pass data to network client data processor
                                if (!NetworkClientDataProcessor.ReceivedBytes(buffer, received, iteration++))
                                {
                                    Logger.LogInformation("{ClientName} - Informed by data processor to stop receiving", clientName);
                                    break;
                                }

                                // Check cancellation
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                                    break;
                                }
                            }
                            while (received != 0 && clientSocket.Available > 0);

                            // Check cancellation
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                                break;
                            }

                            // Process received data
                            var data = NetworkClientDataProcessor.GetReceived();
                            NetworkClientDataProcessor.ProcessData(data);

                            // Trigger data received event if needed
                            var ts = DateTime.UtcNow;
                            var remoteEndPoint = clientSocket.RemoteEndPoint;
                            ClientDataReceived?.Invoke(this, new ClientDataReceivedEventArgs
                            {
                                Data = data,
                                Timestamp = ts,
                                RemoteEndPoint = remoteEndPoint
                            });

                            // Check cancellation
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                                break;
                            }

                            // Send acknowledgment to client if needed
                            var ackBytes = NetworkClientDataProcessor.GetAckBytes(data);
                            if (ackBytes?.Length > 0)
                            {
                                Logger.LogInformation("{ClientName} - Sending ACK to {RemoteEndPoint}", clientName, remoteEndPoint);

                                // Send acknowledgment to client
                                await clientStream.WriteAsync(ackBytes, 0, ackBytes.Length, cancellationToken);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Logger.LogWarning("{ClientName} - Client processing canceled", clientName);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "{ClientName} - Error in processing client connection", clientName);

                            // Trigger client error event
                            ClientError?.Invoke(this, new ClientErrorEventArgs
                            {
                                Exception = ex,
                                RemoteEndPoint = clientSocket.RemoteEndPoint,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    } // End - if available

                    // Check cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                        break;
                    }

                    // Disconnect if needed
                    if (!clientSocket.IsConnected())
                    {
                        Logger.LogInformation("{ClientName} - Client disconnected", clientName);
                        break;
                    }

                    // Wait a bit
                    Thread.Sleep(300);

                    // Check cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogWarning("{ClientName} - Cancellation requested for client", clientName);
                        break;
                    }

                    // Disconnect if needed
                    if (!clientSocket.IsConnected())
                    {
                        Logger.LogInformation("{ClientName} - Client disconnected", clientName);
                        break;
                    }

                    // Write waiting message
                    if ((loopCntr % 10) == 0 || loopCntr == 0)
                    {
                        Logger.LogDebug("{ClientName} - Waiting on data; currently on [{LoopCounter}] iteration", clientName, loopCntr);

                        ClientWaiting?.Invoke(this, new ClientWaitingEventArgs
                        {
                            RemoteEndPoint = clientSocket?.RemoteEndPoint,
                            Timestamp = DateTime.UtcNow
                        });
                    }

                    // Increment loop counter; reset if needed
                    loopCntr += 1;
                    if (loopCntr >= (uint.MaxValue - 1))
                    {
                        loopCntr = 0;
                    }
                } // End - while true
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{ClientName} - Error in client processing", clientName);

                // Trigger client error event
                ClientError?.Invoke(this, new ClientErrorEventArgs
                {
                    Exception = ex,
                    RemoteEndPoint = clientSocket.RemoteEndPoint,
                    Timestamp = DateTime.UtcNow
                });

                throw;
            }

            Logger.LogTrace("{ClientName} - Leaving client connection processing", clientName);
        }

        /// <summary>
        /// Get client stream
        /// </summary>
        /// <param name="clientSocket">The client socket to get stream of</param>
        /// <param name="certificate">Certificate if using secured connection</param>
        /// <returns>Client network stream or SSL stream if valid</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="certificate"/> is null</exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private Stream GetClientStream(Socket clientSocket, X509Certificate? certificate = null)
        {
            if (clientSocket is null)
            {
                throw new ArgumentNullException(nameof(clientSocket));
            }

            // Create network stream
            var networkStream = new NetworkStream(clientSocket);

            if (certificate is null)
            {
                return networkStream;
            }

            // Create SSL steam using network stream and authenticate using certificate
            var sslStream = new SslStream(networkStream, true);

            // Authenticate client as a server
            if (SslProtocols.HasValue)
            {
                sslStream.AuthenticateAsServer(certificate, false, SslProtocols.Value, false);
            }
            else
            {
                sslStream.AuthenticateAsServer(certificate);
            }

            return sslStream;
        }

        /// <summary>
        /// Invoke client disconnected event
        /// </summary>
        /// <param name="clientSocket">The socket that disconnected</param>
        /// <param name="ex">Optional exception that caused the disconnection</param>
        private void OnClientDisconnected(Socket clientSocket, Exception? ex = null)
        {
            // Invoke client disconnect if possible
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs
            {
                Exception = ex,
                RemoteEndPoint = clientSocket?.RemoteEndPoint,
                Timestamp = DateTime.UtcNow
            });
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
                Logger.LogTrace("Starting clients thread monitor");
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
                    Logger.LogTrace("Monitor thread canceled");
                }
                catch (ThreadInterruptedException)
                {
                    Logger.LogTrace("Monitor thread interrupted during wait");
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
            Logger.LogTrace("Monitoring clients");

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
                        Logger.LogWarning("Could not get lock on object to see if we need to continue to monitor client threads");
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
                Logger.LogTrace("Monitor delay canceled");
            }
            finally
            {
                Logger.LogTrace("Leaving client monitor");
            }
        }

        /// <summary>
        /// Clean up client threads that have stopped
        /// </summary>
        private void CleanUpStoppedClientThreads()
        {
            if (_clientThreads.Count > 0)
            {
                // Lock it up to work on removal
                if (Monitor.TryEnter(_lock))
                {
                    try
                    {
                        // New-up threads to remove container
                        var removeThese = new List<ClientThreadMeta>();

                        Logger.LogTrace("Checking for stopped client threads");

                        // Get threads to remove
                        for (var i = 0; i < _clientThreads.Count; i++)
                        {
                            var meta = _clientThreads.ElementAt(i);
                            if (meta.Thread.ThreadState == ThreadState.Stopped)
                            {
                                removeThese.Add(meta);
                            }
                        }

                        // Remove threads from container
                        if (removeThese.Count > 0)
                        {
                            Logger.LogTrace("Removing stopped client threads");

                            // Remove threads
                            foreach (var meta in removeThese)
                            {
                                // Remove from list
                                _clientThreads.Remove(meta);
                            }
                        }

                        // Call garbage collector
                        if (removeThese.Count > 0)
                        {
                            GC.Collect();
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                }
                else
                {
                    Logger.LogWarning("Could not get lock on object to clean up client threads");
                }
            }
        }

        /// <summary>
        /// Cancel running client threads and remove them from monitoring
        /// </summary>
        private void CancelAndRemoveClientThreads()
        {
            // Lock it up to work on removal
            lock (_lock)
            {
                // Test if there are any clients to cancel and remove
                if (_clientThreads.Count > 0)
                {
                    try
                    {
                        Logger.LogTrace("Canceling client threads");

                        // Get threads to remove
                        for (var i = 0; i < _clientThreads.Count; i++)
                        {
                            // Get meta
                            var meta = _clientThreads.ElementAt(i);

                            // Attempt to stop 
                            try
                            {
                                meta.CancellationTokenSource.Cancel();
                            }
                            catch
                            {
                                Logger.LogWarning("Could not cancel client thread, {ClientName}", meta.Name);
                            }

                            // Remove from list
                            _clientThreads.Remove(meta);
                        }
                    }
                    finally
                    {
                        GC.Collect();
                    }
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
            // Cancel and remove client threads
            CancelAndRemoveClientThreads();

            // Cancel
            _cts.Cancel();

            // Wait
            Thread.Sleep(200);

            // Handle client monitor thread
            if (_monitorThread != null)
            {
                if (_monitorThread.ThreadState == ThreadState.WaitSleepJoin)
                {
                    _monitorThread.Interrupt();
                }

                // Wait
                Thread.Sleep(100);

                _monitorThread = null;
            }

            // Dispose listener
            if (ListenerSocket != null)
            {
                ListenerSocket.Shutdown(SocketShutdown.Both);
                ListenerSocket.Close();
                ListenerSocket.Dispose();
                ListenerSocket = null;
            }
        }
    }
}
