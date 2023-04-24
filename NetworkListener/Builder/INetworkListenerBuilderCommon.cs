namespace NetworkListener.Builder
{
    using System.Net.Sockets;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using System.Security.Authentication;

    /// <summary>
    /// Common interface for a faceted <see cref="NetworkListener"/> object builder
    /// </summary>
    public interface INetworkListenerBuilderCommon
    {
        /// <summary>
        /// With specified IP address
        /// </summary>
        /// <param name="ipAddress">The IP address to use</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithIPAddress(IPAddress ipAddress);
        
        /// <summary>
        /// With specified socket type
        /// </summary>
        /// <param name="socketType">The socket type to use</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithSocketType(SocketType socketType);

        /// <summary>
        /// With specified protocol type
        /// </summary>
        /// <param name="protocolType">The protocol type to use</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithProtocol(ProtocolType protocolType);

        /// <summary>
        /// With certificate and security protocol to use for secure communications
        /// </summary>
        /// <param name="certificate">The certificate to use for secure communications</param>
        /// <param name="sslProtocols">The secure protocols to use for secure communications; defaults is null- letting environment decide</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithCert(X509Certificate certificate, SslProtocols? sslProtocols);

        /// <summary>
        /// Max client connections
        /// </summary>
        /// <param name="maxClientConnections">Maximum number of connections to handle</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithMaxClientConnections(int maxClientConnections);

        /// <summary>
        /// With handling parallel connections
        /// </summary>
        /// <param name="handleParallelConnections">Flag to indicate that network listener should handle parallel connections</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon WithHandleParallelConnections(bool handleParallelConnections);

        /// <summary>
        /// Build the <see cref="NetworkListener"/> object as informed
        /// </summary>
        /// <returns>Configured listener object</returns>
        NetworkListener Build();
    }
}

