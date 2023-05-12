namespace NetworkListenerCore
{
    using Microsoft.Extensions.Logging;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System.Net;
    using System.Net.Sockets;
    using System.Xml.Linq;

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

            try
            {
                // Check for cancellation
                if (CancellationToken.IsCancellationRequested)
                {
                    return ClientThreadMeta.None;
                }

                // Generate client data processor
                var clientDataProcessor = ClientDataProcessorFactory.Invoke();

                // Build buffer
                var buffer = new byte[clientDataProcessor.MaxBufferSize];

                // Receive bytes of data
                var networkResults = await serverSocket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint, CancellationToken);

                // Check for cancellation
                if (CancellationToken.IsCancellationRequested)
                {
                    return ClientThreadMeta.None;
                }

                // Cast remote client end-point
                remoteIpEndPoint = networkResults.RemoteEndPoint as IPEndPoint;

                // Client cancellation token source
                var cts = new CancellationTokenSource();

                // Create new client thread to handle further procssing
                var thread = new Thread(() =>
                {
                    // Process the client connection and wait.
                    var triggeredOnClientDisconnected = false;
                    try
                    {
                        // Wait for client processing
                        var task = new Task(async () =>
                        {
                            // Init client data processor
                            clientDataProcessor.Initialize(remoteIpEndPoint!);

                            // Check for cancellation
                            if (CancellationToken.IsCancellationRequested)
                            {
                                Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                                return;
                            }

                            // Send bytes to client data processor
                            try
                            {
                                clientDataProcessor.ReceiveBytes(buffer, networkResults.ReceivedBytes, 1);
                            }
                            catch (Exception ex)
                            {
                                var errMsg = "ERR-NCDP-03: Error in client data processor received bytes call";
                                Logger.LogError(ex, errMsg);

                                // Trigger client error event
                                OnClientError(remoteIpEndPoint, new AggregateException(errMsg, ex));
                            }

                            // Check for cancellation
                            if (CancellationToken.IsCancellationRequested)
                            {
                                Logger.LogWarning("Client [{ClientRemoteEndPoint}] - Cancellation requested for client", remoteIpEndPoint);
                                return;
                            }

                            // Get client data and allow processor to process it
                            try
                            {
                                // Get data
                                var data = clientDataProcessor.GetData();

                                // Process received data
                                clientDataProcessor.ProcessData(data);

                                // Trigger data received event if needed
                                OnClientDataReceived(remoteEndPoint, data);
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
                                    buffer = null;

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
                        });
                        Task.WaitAll(new Task[] { task }, CancellationToken);
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
    }
}
