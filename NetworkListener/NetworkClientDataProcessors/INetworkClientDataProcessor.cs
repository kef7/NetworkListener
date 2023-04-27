using System.Net;

namespace NetworkListener.NetworkClientDataProcessors
{
    /// <summary>
    /// Interface for client data processing used by <see cref="NetworkListener"/> to processes 
    /// bytes received from clients and ACK bytes sent to clients connected to the listener
    /// </summary>
    /// <remarks>
    /// The methods below are called in steps defined:
    /// 1 - <see cref="Initialize(EndPoint)"/>, called once before processing bytes/data
    /// 2 - <see cref="ProcessReceivedBytes(byte[], int, int)"/>, called until no more bytes are available to process
    /// 3 - <see cref="GetReceivedData"/>, called after all available bytes have been processed to get full data
    /// 4 - <see cref="ProcessData(object?)"/>, called when full data is present
    /// 5 - <see cref="GetAckBytes(object?)"/>, called after data is processed
    /// </remarks>
    public interface INetworkClientDataProcessor
    {
        /// <summary>
        /// Max buffer size for byte arrays and received data
        /// </summary>
        int MaxBufferSize { get; }

        /// <summary>
        /// Initialize the network client data processor
        /// </summary>
        /// <param name="remoteEndPoint">Client remote endpoint on the network</param>
        void Initialize(EndPoint remoteEndPoint);

        /// <summary>
        /// Process received bytes from the network socket
        /// </summary>
        /// <param name="bytes">Current bytes received from network from the current read</param>
        /// <param name="received">Total number of bytes received from the current read</param>
        /// <param name="iteration">The iteration of processing on the current read</param>
        /// <returns>True if processing should continue; false otherwise</returns>
        bool ProcessReceivedBytes(byte[] bytes, int received, int iteration);

        /// <summary>
        /// Get the received data from all previous calls of received bytes
        /// processed by <see cref="ProcessReceivedBytes(byte[], int, int)"/>
        /// </summary>
        /// <returns>All bytes received decoded into an object</returns>
        object? GetReceivedData();

        /// <summary>
        /// Process the data received from the network socket
        /// </summary>
        /// <param name="data">The data received from the network</param>
        void ProcessData(object? data);

        /// <summary>
        /// Get an acknowledgment in bytes based on the data received from the network
        /// which was processed by <see cref="ProcessReceivedBytes(byte[], int, int)"/>.
        /// </summary>
        /// <param name="data">Data received from network</param>
        /// <returns>Acknowledgment data in bytes, or null/empty-array to send nothing back to client</returns>
        byte[] GetAckBytes(object? data);
    }
}
