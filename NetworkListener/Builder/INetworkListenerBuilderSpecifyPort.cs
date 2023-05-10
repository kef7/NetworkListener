namespace NetworkListenerCore.Builder
{
    /// <summary>
    /// Interface for a faceted <see cref="NetworkListener"/> object builder
    /// forcing port to be defined
    /// </summary>
    public interface INetworkListenerBuilderSpecifyPort
    {
        /// <summary>
        /// Specify the required port number
        /// </summary>
        /// <param name="port">The port number to use</param>
        /// <returns>Ref to builder</returns>
        INetworkListenerBuilderSpecifyNcdpFactory UsingPort(int port);
    }
}

