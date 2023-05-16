namespace NetworkListenerCore
{
    using Microsoft.Extensions.Logging;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    /// <summary>
    /// Server strategy for a connection-less based network client processing; like UDP clients.
    /// </summary>
    internal class ConnectionlessServerStrategy : ServerStrategy
    {
        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="logger">Generic logger</param>
        public ConnectionlessServerStrategy(ILogger logger)
            : base(logger)
        {
        }

        /// <inheritdoc cref="IServerStrategy.InitServer(IPEndPoint, SocketType, ProtocolType)"/>
        public override Socket InitServer(IPEndPoint ipEndPoint, SocketType type, ProtocolType protocolType)
        {
            // New up server socket
            var serverSocket = new Socket(ipEndPoint.AddressFamily, type, protocolType);

            // Configure server socket
            serverSocket.Bind(ipEndPoint);

            return serverSocket;
        }

        /// <inheritdoc cref="IServerStrategy.RunClientThread(Socket, Func{INetworkClientDataProcessor}, CancellationToken)"/>
        public override async Task<ClientThreadMeta> RunClientThread(Socket serverSocket, Func<INetworkClientDataProcessor> networkClientDataProcessorFactory, CancellationToken cancellationToken)
        {
            await base.RunClientThread(serverSocket, networkClientDataProcessorFactory, cancellationToken);

            Logger.LogInformation("Waiting to receive on {EndPoint}", serverSocket.LocalEndPoint);

            // Define a client endpoint for UDP comms sent from any source; will be assigned value later
            var remoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var remoteEndPoint = remoteIpEndPoint as EndPoint;

            // Client cancellation token source
            var cts = new CancellationTokenSource();

            try
            {
                // Generate client data processor
                var clientDataProcessor = ClientDataProcessorFactory.Invoke();

                // Build buffer
                var buffer = BuildClientDataBuffer(clientDataProcessor.MaxBufferSize);

                // Receive bytes of data; block thread
                var received = serverSocket.ReceiveFrom(buffer, SocketFlags.None, ref remoteEndPoint);

                // Cast remote client end-point
                remoteIpEndPoint = remoteEndPoint as IPEndPoint;

                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                    return ClientThreadMeta.None;
                }

                Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Received [{BytesReceived}] bytes", remoteIpEndPoint, received);

                // Init client data processor
                clientDataProcessor.Initialize(remoteIpEndPoint!);

                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                    return ClientThreadMeta.None;
                }

                // Send bytes to client data processor
                try
                {
                    clientDataProcessor.ReceiveBytes(buffer, received, 1);
                }
                catch (Exception ex)
                {
                    var errMsg = "ERR-NCDP-03: Error in client data processor received bytes call";
                    Logger.LogError(ex, errMsg);

                    // Trigger client error event
                    OnClientError(remoteIpEndPoint, new AggregateException(errMsg, ex));
                }

                // Check for cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                    return ClientThreadMeta.None;
                }

                // Create new client thread to handle further processing
                var thread = new Thread(() =>
                {
                    Logger.LogTrace("Client [{ClientRemoteEndPoint}] - Thread running", remoteIpEndPoint);

                    // Process the client connection and wait.
                    var triggeredOnClientDisconnected = false;
                    try
                    {
                        // Wait for client processing
                        Task.WaitAll(new Task[] { ProcessClientData(serverSocket, remoteIpEndPoint, clientDataProcessor, cts.Token) }, cts.Token);
                    }
                    catch (AggregateException aggEx)
                    {
                        // Get base exception
                        var ex = aggEx.GetBaseException();

                        // Trigger client error event
                        OnClientError(remoteIpEndPoint, ex);

                        // Check if canceled
                        if (ex is OperationCanceledException)
                        {
                            Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Processing thread canceled", remoteIpEndPoint);

                            // Trigger client disconnected event
                            OnClientDisconnected(remoteIpEndPoint, ex as OperationCanceledException);
                            triggeredOnClientDisconnected = true;
                        }
                        // Check if disconnected abruptly
                        else if (ex is SocketException sEx && sEx.NativeErrorCode == 10054)
                        {
                            Logger.LogError("Client [{ClientRemoteEndPoint}] - Disconnected abruptly", remoteIpEndPoint);

                            // Trigger client disconnected event
                            OnClientDisconnected(remoteIpEndPoint, sEx);
                            triggeredOnClientDisconnected = true;
                        }
                        else
                        {
                            Logger.LogError(ex, "Client [{ClientRemoteEndPoint}] - Error in client processing", remoteIpEndPoint);
                        }
                    }
                    finally
                    {
                        // Should we trigger client disconnected event
                        if (!triggeredOnClientDisconnected)
                        {
                            // Trigger client disconnected event
                            OnClientDisconnected(remoteIpEndPoint);
                        }
                    }

                    Logger.LogTrace("Client [{ClientRemoteEndPoint}] - Exiting client thread", remoteIpEndPoint);
                });

                // Configure thread
                thread.IsBackground = true;
                thread.Name = Guid.NewGuid().ToString();
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
            catch (Exception ex)
            {
                OnClientError(null, ex);
            }

            return ClientThreadMeta.None;
        }

        /// <summary>
        /// Process client communications
        /// </summary>
        /// <param name="serverSocket">Server socket used to send and received client data</param>
        /// <param name="remoteIpEndPoint">Client remote IP end-point</param>
        /// <param name="clientDataProcessor">The <see cref="INetworkClientDataProcessor"/> used to process the client data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        private async Task ProcessClientData(Socket serverSocket, IPEndPoint? remoteIpEndPoint, INetworkClientDataProcessor clientDataProcessor, CancellationToken cancellationToken)
        {
            // Get client data and allow processor to process it
            try
            {
                // Get data
                var data = clientDataProcessor.GetData();

                // Process received data
                clientDataProcessor.ProcessData(data);

                // Trigger data received event if needed
                OnClientDataReceived(remoteIpEndPoint, data);
            }
            catch (Exception ex)
            {
                var errMsg = "ERR-NCDP-04: Error processing client data";
                Logger.LogError(ex, errMsg);

                // Trigger client error event
                OnClientError(remoteIpEndPoint, new AggregateException(errMsg, ex));
            }

            // Check cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                return;
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
                        Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                        break;
                    }

                    // Declare buffer
                    var buffer = new byte[0];

                    // Get send bytes from client data processor
                    continueToSend = clientDataProcessor.SendBytes(out buffer, totalSent, sendIteration);

                    // Send bytes to client if needed
                    if (buffer?.Length > 0)
                    {
                        Logger.LogInformation("Client [{ClientRemoteEndPoint}] - Sending data on iteration [{Iteration}]", remoteIpEndPoint, sendIteration);

                        // Send acknowledgment to client
                        var sendResults = await serverSocket.SendToAsync(buffer, SocketFlags.None, remoteIpEndPoint!);

                        Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Sending [{BytesSent}] bytes on iteration [{Iteration}]", remoteIpEndPoint, buffer.Length, sendIteration);

                        // Increment total sent
                        totalSent += buffer.Length;
                    }

                    // Increment iteration
                    sendIteration += 1;
                }
                while (continueToSend);

                Logger.LogDebug("Client [{ClientRemoteEndPoint}] - Sent [{BytesSent}] in total", remoteIpEndPoint, totalSent);

                // Check cancellation
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                    return;
                }
            }
            catch (Exception ex)
            {
                var errMsg = "ERR-NCDP-05: Error retrieving client data processor send bytes";
                Logger.LogError(ex, errMsg);

                // Trigger client error event
                OnClientError(remoteIpEndPoint, new AggregateException(errMsg, ex));
            }
        }
    }
}
