namespace NetworkListenerCore.ServerStrategies
{
    using Microsoft.Extensions.Logging;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstract base server strategy for initialization and processing server the server tasks
    /// </summary>
    internal abstract class ServerStrategy : IServerStrategy
    {
        /// <summary>
        /// Logger
        /// </summary>
        protected virtual ILogger Logger { get; }

        /// <summary>
        /// Cancellation token
        /// </summary>
        protected CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        /// <summary>
        /// The network client data processor factory that produces client data processors for each client connection
        /// </summary>
        public Func<INetworkClientDataProcessor> ClientDataProcessorFactory { get; internal set; } = null!;

        /// <inheritdoc cref="IClientEvents.ClientConnected"/>
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected = null;

        /// <inheritdoc cref="IClientEvents.ClientDataReceived"/>
        public event EventHandler<ClientDataReceivedEventArgs>? ClientDataReceived = null;

        /// <inheritdoc cref="IClientEvents.ClientDisconnected"/>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected = null;

        /// <inheritdoc cref="IClientEvents.ClientError"/>
        public event EventHandler<ClientErrorEventArgs>? ClientError = null;

        /// <summary>
        /// CTOR
        /// </summary>
        public ServerStrategy(ILogger logger)
        {
            Logger = logger;
        }

        /// <inheritdoc cref="IServerStrategy.InitServer(IPEndPoint, SocketType, ProtocolType)"/>
        public abstract Socket InitServer(IPEndPoint ipEndPoint, SocketType type, ProtocolType protocolType);

        /// <inheritdoc cref="IServerStrategy.RunClientThread(Socket, Func{INetworkClientDataProcessor}, CancellationToken)"/>
        public virtual async Task<ClientThreadMeta> RunClientThread(Socket serverSocket, Func<INetworkClientDataProcessor> networkClientDataProcessorFactory, CancellationToken cancellationToken)
        {
            ClientDataProcessorFactory = networkClientDataProcessorFactory;
            CancellationToken = cancellationToken;

            return await Task.FromResult(ClientThreadMeta.None);
        }

        /// <summary>
        /// Build client data byte buffer
        /// </summary>
        /// <param name="size">Size of byte buffer</param>
        /// <returns>Byte array of size <paramref name="size"/>; if <paramref name="size"/> is 
        /// greater than <see cref="IServerStrategy.MAX_BUFFER_SIZE"/> or zero, this will return a byte array of  
        /// size <see cref="IServerStrategy.MAX_BUFFER_SIZE"/></returns>
        public virtual byte[] BuildClientDataBuffer(int size)
        {
            if (size < 0 || size > IServerStrategy.MAX_BUFFER_SIZE)
            {
                return new byte[IServerStrategy.MAX_BUFFER_SIZE];
            }

            return new byte[size];
        }

        /// <summary>
        /// Invoke client connected event
        /// </summary>
        /// <param name="remoteEndPoint">The client remote end-point</param>
        protected virtual void OnClientConnected(EndPoint? remoteEndPoint)
        {
            // Invoke client connect if possible
            ClientConnected?.Invoke(this, new ClientConnectedEventArgs
            {
                RemoteEndPoint = remoteEndPoint,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Invoke client data received event
        /// </summary>
        /// <param name="remoteEndPoint">The client remote end-point</param>
        /// <param name="data">Data received from client</param>
        protected virtual void OnClientDataReceived(EndPoint? remoteEndPoint, object? data = null)
        {
            // Invoke client connect if possible
            ClientDataReceived?.Invoke(this, new ClientDataReceivedEventArgs
            {
                Data = data,
                RemoteEndPoint = remoteEndPoint,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Invoke client disconnected event
        /// </summary>
        /// <param name="remoteEndPoint">The client remote end-point</param>
        /// <param name="ex">Optional exception that caused the disconnection</param>
        protected virtual void OnClientDisconnected(EndPoint? remoteEndPoint, Exception? ex = null)
        {
            // Invoke client disconnect if possible
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs
            {
                Exception = ex,
                RemoteEndPoint = remoteEndPoint,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Invoke client error event
        /// </summary>
        /// <param name="remoteEndPoint">The client remote end-point</param>
        /// <param name="ex">Optional exception caught during client processing</param>
        protected virtual void OnClientError(EndPoint? remoteEndPoint, Exception? ex = null)
        {
            // Invoke client error event
            ClientError?.Invoke(this, new ClientErrorEventArgs
            {
                Exception = ex,
                RemoteEndPoint = remoteEndPoint,
                Timestamp = DateTime.UtcNow
            });
        }

        public virtual void Dispose()
        {
        }
    }
}
