namespace NetworkListener.NetworkClientDataProcessors
{
    /// <summary>
    /// Interface for client data processing used by <see cref="NetworkListener"/> to processes 
    /// bytes received from clients and ACK bytes sent to clients connected to the listener
    /// </summary>
    public interface INetworkClientDataProcessor
    {
        /// <summary>
        /// Max buffer size for byte arrays and received data
        /// </summary>
        int MaxBufferSize { get; }

        /// <summary>
        /// Process received bytes from the network socket
        /// </summary>
        /// <param name="bytes">Current bytes received from network from the current read</param>
        /// <param name="received">Total number of bytes received from the current read</param>
        /// <param name="iteration">The iteration of processing on the current read</param>
        /// <returns>True if processing should continue; false otherwise</returns>
        bool ReceivedBytes(byte[] bytes, int received, int iteration);

        /// <summary>
        /// Get the received data as object from all previous sets of received bytes
        /// processed by <see cref="ReceivedBytes(byte[], int, int)"/>
        /// </summary>
        /// <returns>All bytes received decoded into an object</returns>
        object? GetReceived();

        /// <summary>
        /// Process the data received from the network socket
        /// </summary>
        /// <param name="data">The data received from the network</param>
        void ProcessReceived(object? data);

        /// <summary>
        /// Get an acknowledgment in bytes based on the data received from the network
        /// which was processed by <see cref="ReceivedBytes(byte[], int, int)"/>.
        /// </summary>
        /// <param name="data">Data received from network</param>
        /// <returns>Acknowledgment data in bytes, or null/empty-array to send nothing back to client</returns>
        byte[] GetAckBytes(object? data);
    }
}
