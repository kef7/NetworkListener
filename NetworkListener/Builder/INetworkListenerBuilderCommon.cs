namespace NetworkListener.Builder
{
    using System.Net.Sockets;
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    using System.Security.Authentication;

    public interface INetworkListenerBuilderCommon
    {
        INetworkListenerBuilderCommon WithIPAddress(IPAddress ipAddress);
        INetworkListenerBuilderCommon WithSocketType(SocketType socketType);
        INetworkListenerBuilderCommon WithProtocol(ProtocolType protocolType);
        INetworkListenerBuilderCommon WithCert(X509Certificate certificate, SslProtocols? sslProtocols);
        INetworkListenerBuilderCommon WithMaxClientConnections(int maxClientConnections);
        INetworkListenerBuilderCommon WithHandleParallelConnections(bool handleParallelConnections);
        NetworkListener Build();
    }
}

