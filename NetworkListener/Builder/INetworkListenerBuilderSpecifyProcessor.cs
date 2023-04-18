namespace NetworkListener.Builder
{
    using global::NetworkListener.NetworkCommunicationProcessors;

    public interface INetworkListenerBuilderSpecifyProcessor
    {
        INetworkListenerBuilderCommon UsingProcessor(INetworkCommunicationProcessor networkCommunicationProcessor);
    }
}

