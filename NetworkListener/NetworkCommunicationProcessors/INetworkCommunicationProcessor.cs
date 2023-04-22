namespace NetworkListener.NetworkCommunicationProcessors
{
    using System.Net;

    // TODO: Should this allow for the access to the reading of the stream so we can get large sets of data? Or access to the stream for read & write.
    // In this way we will allow each one to use the stream for reading until a character(s) is found like, "<EOF>".

    /// <summary>
    /// Interface for a network communication processing class
    /// </summary>
    public interface INetworkCommunicationProcessor
    {
        /// <summary>
        /// Max buffer size for byte arrays and received data
        /// </summary>
        int MaxBufferSize { get; }

        /// <summary>
        /// Used to encode the bytes received from the network into a string of characters
        /// </summary>
        /// <param name="data">Bytes to be encoded into a string</param>
        /// <returns>String message from encoded byte array @ <paramref name="data"/></returns>
        string Encode(byte[] data);

        /// <summary>
        /// Process the message from the network socket. Happens after <see cref="Decode(byte[])"/>.
        /// </summary>
        /// <param name="message">The decoded network message</param>
        /// <param name="timeStamp">The time stamp of this communication message</param>
        /// <param name="remoteEndpoint">The remote endpoint that we are communicating with</param>
        /// <returns>An acknowledgment message to send back through network socket. Return null to not send anything back.</returns>
        string ProcessCommunication(string message, DateTime timeStamp, EndPoint? remoteEndpoint);

        /// <summary>
        /// Used to decode an acknowledgment message to be sent to the network socket from <see cref="ProcessCommunication(string)"/>. Happens after <see cref="ProcessCommunication(string)"/>.
        /// </summary>
        /// <param name="message">The message to be decoded into a byte array</param>
        /// <returns>A byte array that is the message to be sent through the network socket; null to not send anything</returns>
        byte[] Decode(string message);
    }
}
