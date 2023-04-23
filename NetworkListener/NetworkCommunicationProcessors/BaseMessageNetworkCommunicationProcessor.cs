using Microsoft.Extensions.Logging;
using System.Text;

namespace NetworkListener.NetworkCommunicationProcessors
{
    /// <summary>
    /// Base simple network communication processor; for communications that contain end of message processing marker and ack
    /// </summary>
    public abstract class BaseMessageNetworkCommunicationProcessor : INetworkCommunicationProcessor
    {
        /// <summary>
        /// Logger
        /// </summary>
        protected ILogger Logger { get; }

        /// <inheritdoc cref="INetworkCommunicationProcessor.MaxBufferSize"/>
        public int MaxBufferSize { get; init; } = 4096 * 4096;

        /// <summary>
        /// Characters marker to flag the end of network buffer processing
        /// </summary>
        public virtual string EndOfProcessingMarker { get; init; } = "<EOF>";

        /// <summary>
        /// String builder object to hold string representation of message received from network connection
        /// </summary>
        protected virtual StringBuilder MessageBuilder { get; } = new();

        /// <summary>
        /// Character decoder for bytes (buffer) received from network connection
        /// </summary>
        protected virtual Decoder Decoder { get; init; } = Encoding.UTF8.GetDecoder();

        /// <summary>
        /// Character encoder for bytes to send as acknowledgment response to bytes received from network connection
        /// </summary>
        protected virtual Encoder Encoder { get; init; } = Encoding.UTF8.GetEncoder();

        /// <summary>
        /// CTOR for base message network communication processor; message should contain an 
        /// end of receive character to signal stopping of message processing
        /// </summary>
        /// <param name="logger">Generic logger</param>
        public BaseMessageNetworkCommunicationProcessor(ILogger<BaseMessageNetworkCommunicationProcessor> logger)
        {
            Logger = logger;
        }

        /// <inheritdoc cref="INetworkCommunicationProcessor.ReceivedBytes(byte[], int, int)"/>
        public virtual bool ReceivedBytes(byte[] bytes, int received, int iteration)
        {
            Logger.LogTrace("Received {Received} bytes on iteration [{Iteration}]", received, iteration);

            // Reset processing if on first iteration
            if (iteration == 1)
            {
                ResetBufferProcessing();
            }

            // Check buffer
            if (bytes.Length != 0)
            {
                // Get decoded chars from buffer
                var chars = new char[Decoder.GetCharCount(bytes, 0, received)];
                Decoder.GetChars(bytes, 0, received, chars, 0);

                // Append chars to message string builder
                MessageBuilder.Append(chars);
            }

            // Check for end of processing marker
            if (MessageBuilder.ToString().IndexOf(EndOfProcessingMarker) != -1)
            {
                Logger.LogTrace("End of processing marker found on iteration [{Iteration}]", iteration);

                // Return false to stop processing more
                return false;
            }

            // Return true to continue processing if there is more
            return true;
        }

        /// <inheritdoc cref="INetworkCommunicationProcessor.GetReceived"/>
        public virtual object? GetReceived()
        {
            return MessageBuilder.ToString();
        }

        /// <inheritdoc cref="INetworkCommunicationProcessor.ProcessReceived(object?)"/>
        public abstract void ProcessReceived(object? data);

        /// <inheritdoc cref="INetworkCommunicationProcessor.GetAckBytes(object?)"/>
        public virtual byte[] GetAckBytes(object? data)
        {
            // Build ACK message
            var ack = BuildAckMessage(data);

            // Get bytes from ack message
            var chars = ack.ToCharArray();
            var bytes = new byte[Encoder.GetByteCount(chars, 0, chars.Length, false)];
            Encoder.GetBytes(chars, 0, chars.Length, bytes, 0, false);

            // Reset processing 
            ResetBufferProcessing();

            return bytes;
        }

        /// <summary>
        /// Build the ACK message based on current processed message
        /// </summary>
        /// <param name="data">Data received from network</param>
        /// <returns>The ACK message to send</returns>
        protected virtual string BuildAckMessage(object? data)
        {
            // Get current message
            var processedMessage = data as string;
            if (!string.IsNullOrWhiteSpace(processedMessage))
            {
                // Return ACK
                return "<ACK>";
            }

            // Return NACK
            return "<NACK>";
        }

        /// <summary>
        /// Reset properties used to process the buffered message from the network connection
        /// </summary>
        public virtual void ResetBufferProcessing()
        {
            Decoder.Reset();
            Encoder.Reset();
        }
    }
}
