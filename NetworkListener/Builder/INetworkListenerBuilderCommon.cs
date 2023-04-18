namespace NetworkListener.Builder
{
    using System.Net.Sockets;
    using System.Net;

    public interface INetworkListenerBuilderCommon
    {
        INetworkListenerBuilderCommon WithIPAddress(IPAddress ipAddress);
        INetworkListenerBuilderCommon WithSocketType(SocketType socketType);
        INetworkListenerBuilderCommon WithProtocol(ProtocolType protocolType);
        INetworkListenerBuilderCommon WithMaxClientConnections(int maxClientConnections);
        INetworkListenerBuilderCommon WithHandleParallelConnections(bool handleParallelConnections);
        NetworkListener Build();
    }
}

