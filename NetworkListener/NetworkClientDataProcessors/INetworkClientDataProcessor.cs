namespace NetworkListenerCore.NetworkClientDataProcessors
{
    using System.Net;

    /// <summary>
    /// Interface for client data processing used by <see cref="NetworkListener"/> to processes 
    /// bytes received from clients and ACK bytes sent to clients connected to the listener
    /// </summary>
    /// <remarks>
    /// The methods below are called in steps defined:
    /// 1 - <see cref="Initialize(EndPoint)"/>, called once before processing bytes/data
    /// 2 - <see cref="ReceiveBytes(in byte[], in int, in int)"/>, called until no more bytes are available to process
    /// 3 - <see cref="GetData"/>, called when all bytes received
    /// 4 - <see cref="ProcessData"/>, called when full data is present
    /// 5 - <see cref="SendBytes(out byte[], in int, in int)"/>, called after data is processed, back to step 2
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
        /// Receive bytes from the client socket
        /// </summary>
        /// <param name="bytes">Current bytes received from client for current read</param>
        /// <param name="received">Total number of bytes received for current read</param>
        /// <param name="iteration">The current read iteration count</param>
        /// <returns>True if receiving bytes should continue; false otherwise</returns>
        bool ReceiveBytes(in byte[] bytes, in int received, in int iteration);

        /// <summary>
        /// Get data from all received bytes gathered during <see cref="ReceiveBytes(in byte[], in int, in int)"/>
        /// </summary>
        /// <returns>Data from all received bytes</returns>
        object? GetData();

        /// <summary>
        /// Process data received from the client socket during <see cref="ReceiveBytes(in byte[], in int, in int)"/>
        /// </summary>
        void ProcessData(object? data);

        /// <summary>
        /// Send bytes to the client socket
        /// </summary>
        /// <param name="bytes">Current set of bytes to send to client</param>
        /// <param name="sent">Total number of bytes sent on all previous send calls</param>
        /// <param name="iteration">The current send iteration count</param>
        /// <returns>True if sending bytes should continue; false otherwise</returns>
        bool SendBytes(out byte[] bytes, in int sent, in int iteration);
    }
}
