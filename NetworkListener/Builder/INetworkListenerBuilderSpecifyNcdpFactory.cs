namespace NetworkListener.Builder
{
    using global::NetworkListener.NetworkClientDataProcessors;

    /// <summary>
    /// Interface for a faceted <see cref="NetworkListener"/> object builder
    /// forcing a factory function like <see cref="Func{INetworkClientDataProcessor}"/> to be injected
    /// </summary>
    public interface INetworkListenerBuilderSpecifyNcdpFactory
    {
        /// <summary>
        /// Specify the required network client data processor object
        /// </summary>
        /// <param name="ncdpFactory">Network client data processor factory to generate client data 
        /// processor object for client data processing</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderCommon UsingNcdpFactory(Func<INetworkClientDataProcessor> ncdpFactory);
    }
}

