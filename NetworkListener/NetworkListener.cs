namespace NetworkListenerCore
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;

    /// <summary>
    /// Network listener to listen and respond to client connections
    /// </summary>
    public partial class NetworkListener : IDisposable, IClientEvents
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
        /// Client connection threads
        /// </summary>
        private ICollection<ClientThreadMeta> _clientThreads = new List<ClientThreadMeta>();

        /// <summary>
        /// Clients monitor thread
        /// </summary>
        private Thread? _monitorThread = null;

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
        /// Client data received event signature
        /// </summary>
        public event EventHandler<ClientDataReceivedEventArgs>? ClientDataReceived = null;

        /// <summary>
        /// Client disconnected event signature
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected = null;

        /// <summary>
        /// Client error event signature
        /// </summary>
        public event EventHandler<ClientErrorEventArgs>? ClientError = null;

        /// <summary>
        /// Count of the number of client threads
        /// </summary>
        public int ClientThreadCount
        {
            get
            {
                lock (_lock)
                {
                    return _clientThreads.Count;
                }
            }
        }

        /// <summary>
        /// Backing field to property <see cref="MaxClientConnections"/>
        /// </summary>
        private int _maxClientConnections = 7300;

        /// <summary>
        /// Cancellation token source so we can attempt to have some control
        /// </summary>
        private CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Logger factory
        /// </summary>
        internal ILoggerFactory LoggerFactory { get; }

        /// <summary>
        /// Logger; mainly for trace logging
        /// </summary>
        internal ILogger Logger { get; }

        /// <summary>
        /// The server socket (listener socket)
        /// </summary>
        protected Socket? ServerSocket { get; set; } = null;

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
        public SocketType SocketType { get; internal set; } = SocketType.Stream; // TODO: Switch to custom supported type (Stream, Dgram)

        /// <summary>
        /// The protocol type used on the listener
        /// </summary>
        public ProtocolType ProtocolType { get; internal set; } = ProtocolType.Tcp; // TODO: Switch to custom supported type (TCP, UDP, +)

        /// <summary>
        /// The certificate to use for SSL/TLS communications
        /// </summary>
        public X509Certificate? Certificate { get; internal set; } = null;

        /// <summary>
        /// The secure protocol types to use for secured communications
        /// </summary>
        public SslProtocols? SslProtocols { get; internal set; } = null;

        /// <summary>
        /// The network client data processor factory that produces client data processors for each client connection
        /// </summary>
        public Func<INetworkClientDataProcessor> ClientDataProcessorFactory { get; internal set; } = null!;

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
        /// <param name="loggerFactory">Logger factory used to create loggers</param>
        internal NetworkListener(ILoggerFactory? loggerFactory = null)
        {
            LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            Logger = LoggerFactory.CreateLogger<NetworkListener>();

            CancellationToken = _cts.Token;
        }

        /// <summary>
        /// Start listening, accepting connections, and receiving data
        /// </summary>
        /// <returns></returns>
        public async Task Listen(CancellationToken? cancellationToken = null)
        {
            // Cancellation token set
            bool usedAnotherCancellationToken = false;
            if (cancellationToken is not null)
            {
                CancellationToken = cancellationToken.Value;
                usedAnotherCancellationToken = true;
            }

            // Vars
            IPEndPoint? ipEndPoint = null;
            IServerStrategy serverStrategy = GetServerStrategy();

            try
            {
                // Init and configure listener socket
                try
                {
                    // Stop current socket listening if needed
                    if (ServerSocket is not null)
                    {
                        Logger.LogDebug("Tearing down old server socket");

                        ServerSocket.Close();
                        ServerSocket.Dispose();
                        ServerSocket = null;
                    }

                    Logger.LogDebug("Building new server socket");

                    // Build IP end point
                    ipEndPoint = new IPEndPoint(IPAddress, Port);

                    // Init server socket
                    ServerSocket = serverStrategy.InitServer(ipEndPoint, SocketType, ProtocolType);

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
                    Logger.LogError(ex, "Error in server socket configuration or startup");
                    return; // TODO: should we throw exception here? (custom exception maybe; one that is not caught by outer try-catch)
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

                    // Process connections
                    try
                    {
                        // Check to see if we should handle multiple connections
                        if (!HandleParallelConnections &&
                            ClientThreadCount > 0)
                        {
                            Logger.LogWarning("Handling multiple connections is disabled. Waiting for current connection to close...");

                            // Wait, if we have a client and should not handle multiple
                            WaitForClientProcessing(1);
                        }

                        // Is client count maxed
                        if (HandleParallelConnections &&
                            ClientThreadCount >= MaxClientConnections)
                        {
                            Logger.LogWarning("Max client connections reached; max connections [{MaxClientConnections}]", MaxClientConnections);

                            // Wait, if we have clients and should not handle more at the moment
                            WaitForClientProcessing(MaxClientConnections);
                        }

                        // Process the server socket strategy
                        var ctMeta = await serverStrategy.RunClientThread(ServerSocket, ClientDataProcessorFactory, CancellationToken);

                        // Handle meta
                        if (ctMeta != ClientThreadMeta.None)
                        {
                            lock (_lock)
                            {
                                // Increment client count
                                clientCntr += 1;

                                // Add new client thread meta to tracking
                                _clientThreads.Add(ctMeta);
                            }

                            // Kick off thread monitor
                            StartClientThreadMonitoring();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.LogWarning("Server processing canceled");
                    }
                    catch (Exception loopEx)
                    {
                        Logger.LogError(loopEx, "Error in server processing");
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

                Logger.LogTrace("Leaving server processing");

                // Trigger stop event
                Stopped?.Invoke(this, new ListenerEventArgs
                {
                    LocalEndPoint = ipEndPoint,
                    HostName = HostName,
                    Timestamp = DateTime.UtcNow
                });

                // Dispose server strategy
                DisposeServerStrategy(serverStrategy);
            }
        }

        /// <summary>
        /// Get the server strategy based on current set properties
        /// </summary>
        /// <returns>Server strategy object</returns>
        /// <exception cref="InvalidOperationException">Thrown when supported strategy can not be determined</exception>
        private IServerStrategy GetServerStrategy()
        {
            IServerStrategy? serverStrategy = null;
            
            switch (ProtocolType)
            {
                // Supported connection based protocols
                case ProtocolType.Ipx:
                case ProtocolType.Tcp:
                case ProtocolType.Spx:
                case ProtocolType.SpxII:
                    Logger.LogTrace("Connection strategy selected based on configuration.");
                    serverStrategy = new ConnectionServerStrategy(
                        LoggerFactory.CreateLogger(typeof(NetworkListener)),
                        Certificate,
                        SslProtocols);
                    break;

                    // Supported connection-less based protocols
                case ProtocolType.Idp:
                case ProtocolType.Udp:
                    Logger.LogTrace("Connection-less strategy selected based on configuration.");
                    serverStrategy = new ConnectionlessServerStrategy(
                        LoggerFactory.CreateLogger(typeof(NetworkListener)));
                    break;
            }

            // Hookup events and return server strategy
            if (serverStrategy is not null)
            {
                // Hookup events
                serverStrategy.ClientConnected += ClientConnected;
                serverStrategy.ClientDataReceived += ClientDataReceived;
                serverStrategy.ClientDisconnected += ClientDisconnected;
                serverStrategy.ClientError += ClientError;

                return serverStrategy;
            }

            throw new InvalidOperationException("No strategy defined to handle configured server socket.");
        }

        /// <summary>
        /// Dispose server strategy
        /// </summary>
        /// <param name="serverStrategy">The <see cref="IServerStrategy"/> to be disposed</param>
        private void DisposeServerStrategy(IServerStrategy? serverStrategy)
        {
            if (serverStrategy is null)
            {
                return;
            }

            // Unregister events
            serverStrategy.ClientConnected -= ClientConnected;
            serverStrategy.ClientDataReceived -= ClientDataReceived;
            serverStrategy.ClientDisconnected -= ClientDisconnected;
            serverStrategy.ClientError -= ClientError;

            // Dispose
            serverStrategy.Dispose();
            serverStrategy = null;
        }

        /// <summary>
        /// Wait for clients to process and be removed from thread count
        /// </summary>
        /// <param name="max">Maximum number of client threads to allow</param>
        private void WaitForClientProcessing(int max)
        {
            // Fix max
            if (max < 1)
            {
                max = 1;
            }

            // Wait
            var cnt = ClientThreadCount;
            while (cnt >= max)
            {
                Thread.Sleep(1000);
                cnt = ClientThreadCount;
            }
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
                    if (Monitor.TryEnter(_clientThreads))
                    {
                        try
                        {
                            continueToRun = _clientThreads.Count > 0;
                        }
                        finally
                        {
                            Monitor.Exit(_clientThreads);
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
                                Logger.LogWarning("Could not cancel client thread, Client [{ClientName}]", meta.Name);
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
            if (_monitorThread is not null)
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
            if (ServerSocket is not null)
            {
                ServerSocket.Shutdown(SocketShutdown.Both);
                ServerSocket.Close();
                ServerSocket.Dispose();
                ServerSocket = null;
            }
        }
    }
}
