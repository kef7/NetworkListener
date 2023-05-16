namespace NetworkListenerCore
{
    using NetworkListenerCore.NetworkClientDataProcessors;
    using System.Net;
    using System.Net.Sockets;

    /// <summary>
    /// Server strategy for initialization and processing server the server tasks
    /// </summary>
    internal interface IServerStrategy : IDisposable, IClientEvents
    {
        /// <summary>
        /// Max allowed buffer size for any buffer
        /// </summary>
        public static readonly int MAX_BUFFER_SIZE = Array.MaxLength;

        /// <summary>
        /// Initialize the server socket
        /// </summary>
        /// <param name="ipEndPoint">Endpoint the server is serving responses from</param>
        /// <param name="type">Socket type used by the server</param>
        /// <param name="protocolType">Protocol type used by the server</param>
        /// <returns>The socket used by the server to fulfill requests</returns>
        Socket InitServer(IPEndPoint ipEndPoint, SocketType type, ProtocolType protocolType);

        /// <summary>
        /// Run server processing in new thread for clients
        /// </summary>
        /// <param name="serverSocket">The server socket</param>
        /// <param name="networkClientDataProcessorFactory">The <see cref="INetworkClientDataProcessor"/> used to process client data by this server</param>
        /// <param name="cancellationToken">Cancellation token for canceling processing of the server socket</param>
        /// <returns><see cref="ClientThreadMeta"/> object of client thread meta-data</returns>
        Task<ClientThreadMeta> RunClientThread(Socket serverSocket, Func<INetworkClientDataProcessor> networkClientDataProcessorFactory, CancellationToken cancellationToken);
    }
}
