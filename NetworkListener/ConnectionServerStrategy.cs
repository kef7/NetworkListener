namespace NetworkListenerCore
{
    using Microsoft.Extensions.Logging;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Server strategy for a connection based network client processing; like TCP clients.
    /// </summary>
    internal class ConnectionServerStrategy : ServerStrategy
    {
        /// <summary>
        /// Thread locking object ref
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// The certificate to use for SSL/TLS communications
        /// </summary>
        public X509Certificate? Certificate { get; }

        /// <summary>
        /// The secure protocol types to use for secured communications
        /// </summary>
        public SslProtocols? SslProtocols { get; }

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="certificate">Certificate to use for secure communications</param>
        /// <param name="sslProtocols">SSL/TLS protocols to use for secure communications</param>
        public ConnectionServerStrategy(ILogger logger, X509Certificate? certificate = null, SslProtocols? sslProtocols = null)
            : base(logger)
        {
            Certificate = certificate;

            SslProtocols = sslProtocols;
        }

        /// <inheritdoc cref="IServerStrategy.InitServer(IPEndPoint, SocketType, ProtocolType)"/>
        public override Socket InitServer(IPEndPoint ipEndPoint, SocketType type, ProtocolType protocolType)
        {
            // New up server socket
            var serverSocket = new Socket(ipEndPoint.AddressFamily, type, protocolType);

            // Configure server socket
            serverSocket.Bind(ipEndPoint);

            // Start listening for connections on port
            var port = ipEndPoint.Port;
            serverSocket.Listen(port);

            Logger.LogInformation("Listening on end point {EndPoint}", ipEndPoint);

            return serverSocket;
        }

        /// <inheritdoc cref="IServerStrategy.RunClientThread(Socket, Func{INetworkClientDataProcessor}, CancellationToken)"/>
        public override async Task<ClientThreadMeta> RunClientThread(Socket serverSocket, Func<INetworkClientDataProcessor> networkClientDataProcessorFactory, CancellationToken cancellationToken)
        {
            await base.RunClientThread(serverSocket, networkClientDataProcessorFactory, cancellationToken);

            Logger.LogInformation("Waiting to accept client connections");

            // Accept client connection
            var socket = await serverSocket.AcceptAsync(cancellationToken);

            // Process accepted client connection on a new thread
            if (socket is not null)
            {
                // Trigger client connected event
                OnClientConnected(socket.RemoteEndPoint);

                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    return ClientThreadMeta.None;
                }

                // Client cancellation token source
                var cts = new CancellationTokenSource();

                Logger.LogInformation("Remote client connected from [{ClientRemoteEndPoint}]", socket.RemoteEndPoint);

                // Create new client thread
                var thread = new Thread(() =>
                {
                    // Process the client connection and wait.
                    var triggeredOnClientDisconnected = false;
                    try
                    {
                        // Wait for client processing
                        Task.WaitAll(new Task[] { ProcessClientConnection(socket, cts.Token) }, cts.Token);
                    }
                    catch (AggregateException aggEx)
                    {
                        // Get base exception
                        var ex = aggEx.GetBaseException();

                        // Trigger client error event
                        OnClientError(socket.RemoteEndPoint, ex);

                        // Check if canceled
                        if (ex is OperationCanceledException)
                        {
                            Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Processing thread canceled", socket.RemoteEndPoint);

                            // Trigger client disconnected event
                            OnClientDisconnected(socket.RemoteEndPoint, ex as OperationCanceledException);
                            triggeredOnClientDisconnected = true;
                        }
                        // Check if disconnected abruptly
                        else if (ex is SocketException sEx && sEx.NativeErrorCode == 10054)
                        {
                            Logger.LogError("Client [{ClientRemoteEndPoint}] - Disconnected abruptly", socket.RemoteEndPoint);

                            // Trigger client disconnected event
                            OnClientDisconnected(socket.RemoteEndPoint, sEx);
                            triggeredOnClientDisconnected = true;
                        }
                        else
                        {
                            Logger.LogError(ex, "Client [{ClientRemoteEndPoint}] - Error in client processing", socket.RemoteEndPoint);
                        }
                    }
                    finally
                    {
                        // Get remote end-point
                        var remoteEndPoint = socket.RemoteEndPoint;

                        // Dispose the client connection
                        DisposeClient(socket);

                        // Should we trigger client disconnected event
                        if (!triggeredOnClientDisconnected)
                        {
                            // Trigger client disconnected event
                            OnClientDisconnected(remoteEndPoint);
                        }
                    }
                });

                // Configure thread
                thread.IsBackground = true;
                thread.Name = socket.RemoteEndPoint!.ToString();
                thread.Priority = ThreadPriority.Normal;

                // Build client thread meta
                var ctMeta = new ClientThreadMeta
                {
                    Name = thread.Name!,
                    Thread = thread,
                    CancellationTokenSource = cts
                };

                // Start thread
                thread.Start();

                return ctMeta;
            }

            return ClientThreadMeta.None;
        }

        /// <summary>
        /// Process accepted client socket connection
        /// </summary>
        /// <param name="clientSocket">The accepted client socket</param>
        /// <param name="cancellationToken">A cancellation token to cancel the client socket processing</param>
        /// <returns></returns>
        protected async Task ProcessClientConnection(Socket clientSocket, CancellationToken cancellationToken)
        {
            if (clientSocket is null)
            {
                throw new ArgumentNullException(nameof(clientSocket));
            }

            Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Processing connection", clientSocket.RemoteEndPoint);

            try
            {
                // Get client stream
                using var clientStream = GetClientStream(clientSocket, Certificate);

                // Generate new client data processor to process client data
                INetworkClientDataProcessor clientDataProcessor = null!;
                try
                {
                    clientDataProcessor = ClientDataProcessorFactory.Invoke();
                }
                catch (Exception ex)
                {
                    throw new AggregateException("ERR-NCDP-01: Error generating client data processor", ex);
                }

                // Check for valid client data processor
                if (clientDataProcessor is null)
                {
                    throw new AggregateException($"ERR-NCDP-01: Could not generate client data processor for processing", new NullReferenceException(nameof(clientDataProcessor)));
                }

                // Init client data processor
                try
                {
                    clientDataProcessor.Initialize(clientSocket.RemoteEndPoint!);
                }
                catch (Exception ex)
                {
                    throw new AggregateException("ERR-NCDP-02: Error initializing client data processor", ex);
                }

                // Declare vars and kick off loop to process client
                uint loopCntr = 1;
                while (true)
                {
                    // Check if data available from client
                    if (clientSocket!.Available > 0)
                    {
                        try
                        {
                            // Declare buffer
                            var buffer = BuildClientDataBuffer(clientDataProcessor.MaxBufferSize);

                            // Read all data from client
                            var received = -1;
                            var iteration = 1;
                            while (clientSocket.Available > 0)
                            {
                                Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Receiving data on iteration [{Iteration}]", clientSocket.RemoteEndPoint, iteration);

                                // Receive data from client
                                received = await clientStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                                Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Received [{BytesReceived}] bytes on iteration [{Iteration}]", clientSocket.RemoteEndPoint, received, iteration);

                                // Check cancellation
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                                    break;
                                }

                                // Pass data to network client data processor
                                try
                                {
                                    if (!clientDataProcessor.ReceiveBytes(buffer, received, iteration))
                                    {
                                        Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Informed by data processor to stop receiving", clientSocket.RemoteEndPoint);
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    var errMsg = "ERR-NCDP-03: Error in client data processor received bytes call";
                                    Logger.LogError(ex, errMsg);

                                    // Trigger client error event
                                    OnClientError(clientSocket.RemoteEndPoint, new AggregateException(errMsg, ex));
                                }

                                // Check cancellation
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                                    break;
                                }

                                // Increment iteration
                                iteration += 1;
                            }

                            // Check cancellation
                            if (cancellationToken.IsCancellationRequested)
                            {
                                Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                                break;
                            }

                            // Get client data and allow processor to process it
                            try
                            {
                                // Get data
                                var data = clientDataProcessor.GetData();

                                // Process received data
                                clientDataProcessor.ProcessData(data);

                                // Trigger data received event if needed
                                var ts = DateTime.UtcNow;
                                var remoteEndPoint = clientSocket.RemoteEndPoint;
                                OnClientDataReceived(clientSocket.RemoteEndPoint, data);
                            }
                            catch (Exception ex)
                            {
                                var errMsg = "ERR-NCDP-04: Error processing client data";
                                Logger.LogError(ex, errMsg);

                                // Trigger client error event
                                OnClientError(clientSocket.RemoteEndPoint, new AggregateException(errMsg, ex));
                            }

                            // Get byte data to send to client from client data processor
                            var sendIteration = 1;
                            try
                            {
                                // Iterate over client data send processing
                                var continueToSend = false;
                                var totalSent = 0;
                                do
                                {
                                    // Check cancellation
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                                        break;
                                    }

                                    // Declare buffer
                                    buffer = null;

                                    // Get send bytes from client data processor
                                    continueToSend = clientDataProcessor.SendBytes(out buffer, totalSent, sendIteration);

                                    // Send bytes to client if needed
                                    if (buffer?.Length > 0)
                                    {
                                        Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Sending data on iteration [{Iteration}]", clientSocket.RemoteEndPoint, sendIteration);

                                        // Send acknowledgment to client
                                        await clientStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);

                                        Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Sending [{BytesSent}] bytes on iteration [{Iteration}]", clientSocket.RemoteEndPoint, buffer.Length, sendIteration);

                                        // Increment total sent
                                        totalSent += buffer.Length;
                                    }

                                    // Increment iteration
                                    sendIteration += 1;
                                }
                                while (continueToSend);

                                Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Sent [{BytesSent}] in total", clientSocket.RemoteEndPoint, totalSent);

                                // Check cancellation
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                var errMsg = "ERR-NCDP-05: Error retrieving client data processor send bytes";
                                Logger.LogError(ex, errMsg);

                                // Trigger client error event
                                OnClientError(clientSocket.RemoteEndPoint, new AggregateException(errMsg, ex));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Processing canceled", clientSocket.RemoteEndPoint);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Client [{ClientRemoteEndPoint}] - Error in processing client connection", clientSocket.RemoteEndPoint);

                            // Trigger client error event
                            OnClientError(clientSocket.RemoteEndPoint, ex);
                        }
                    } // End - if available

                    // Check cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                        break;
                    }

                    // Disconnect if needed
                    if (!clientSocket.IsConnected())
                    {
                        Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Disconnected", clientSocket.RemoteEndPoint);
                        break;
                    }

                    // Wait a bit
                    Thread.Sleep(300);

                    // Check cancellation
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", clientSocket.RemoteEndPoint);
                        break;
                    }

                    // Disconnect if needed
                    if (!clientSocket.IsConnected())
                    {
                        Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Disconnected", clientSocket.RemoteEndPoint);
                        break;
                    }

                    // Write waiting message
                    if ((loopCntr % 10) == 0 || loopCntr == 0)
                    {
                        Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Waiting for data; currently on wait [{LoopCounter}]", clientSocket.RemoteEndPoint, loopCntr);
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
                Logger.LogError(ex, "Client [{ClientRemoteEndPoint}] - Error in client processing", clientSocket.RemoteEndPoint);

                // Trigger client error event
                OnClientError(clientSocket.RemoteEndPoint, ex);

                throw;
            }

            Logger.LogTrace("Client [{ClientRemoteEndPoint}] - Leaving client connection processing", clientSocket.RemoteEndPoint);
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
        /// Dispose client socket
        /// </summary>
        /// <param name="clientSocket">The client socket to dispose off</param>
        private void DisposeClient(Socket? clientSocket)
        {
            if (clientSocket is not null)
            {
                Logger.LogTrace("Disposing client socket for remote endpoint {RemoteEndpoint}", clientSocket.RemoteEndPoint);
                clientSocket.Close();
                clientSocket.Dispose();
                clientSocket = null;
            }
        }
    }
}