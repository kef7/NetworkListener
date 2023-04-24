namespace NetworkListener.Builder
{
    using global::NetworkListener.NetworkClientDataProcessors;

    /// <summary>
    /// Interface for a faceted <see cref="NetworkListener"/> object builder
    /// forcing a <see cref="INetworkClientDataProcessor"/> implemented object to be injected
    /// </summary>
    public interface INetworkListenerBuilderSpecifyProcessor
    {
        /// <summary>
        /// Specify the required network client data processor object
        /// </summary>
        /// <param name="networkClientDataProcessor">network client data processor object for the network lister</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon UsingProcessor(INetworkClientDataProcessor networkClientDataProcessor);
    }
}

