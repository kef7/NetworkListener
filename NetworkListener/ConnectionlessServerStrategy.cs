namespace NetworkListenerCore
{
    using Microsoft.Extensions.Logging;
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// Server strategy for a connection-less based network client processing; like UDP clients.
    /// </summary>
    internal class ConnectionlessServerStrategy : IServerStrategy
    {
        protected ILogger Logger { get; }

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
        /// CTOR
        /// </summary>
        /// <param name="logger">Generic logger</param>
        public ConnectionlessServerStrategy(ILogger logger)
        {
            Logger = logger;
        }

        public Socket InitServer(IPEndPoint ipEndPoint, SocketType type, ProtocolType protocolType)
        {
            return null!;
        }

        public async Task<ClientThreadMeta> RunClientThread(Socket serverSocket, Func<INetworkClientDataProcessor> networkClientDataProcessorFactory, CancellationToken cancellationToken)
        {
            await Task.Run(() => { }, cancellationToken);
            return ClientThreadMeta.None;
        }

        public void Dispose()
        {
        }
    }
}
