using Microsoft.Extensions.Logging;
using System.Text;

namespace NetworkListener.NetworkClientDataProcessors
{
    /// <summary>
    /// Base network client data processor for simple messages that contain an end of message processing marker and ack
    /// </summary>
    public abstract class BaseMessageNetworkClientDataProcessor : INetworkClientDataProcessor
    {
        /// <summary>
        /// Check for end marker flag
        /// </summary>
        protected readonly bool _checkForEndMarker = false;

        /// <summary>
        /// Logger
        /// </summary>
        protected ILogger Logger { get; }

        /// <inheritdoc cref="INetworkClientDataProcessor.MaxBufferSize"/>
        public int MaxBufferSize { get; init; } = 4096 * 4096;

        /// <summary>
        /// Characters marker to flag the end of network bytes processing
        /// </summary>
        public virtual string? EndOfProcessingMarker { get; init; } = null;

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
        /// CTOR for base message network client data processor; message should contain an 
        /// end of receive character to signal stopping of message processing
        /// </summary>
        /// <param name="logger">Generic logger</param>
        public BaseMessageNetworkClientDataProcessor(ILogger<BaseMessageNetworkClientDataProcessor> logger)
        {
            Logger = logger;

            // Validate end of processing marker
            if (!string.IsNullOrWhiteSpace(EndOfProcessingMarker))
            {
                _checkForEndMarker = true;
            }
            else
            {
                _checkForEndMarker = false;
                EndOfProcessingMarker = null;
            }
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.ReceivedBytes(byte[], int, int)"/>
        public virtual bool ReceivedBytes(byte[] bytes, int received, int iteration)
        {
            Logger.LogTrace("Received {Received} bytes on iteration [{Iteration}]", received, iteration);

            // Reset processing if on first iteration
            if (iteration == 1)
            {
                ResetProcessing();
            }

            // Check bytes array
            if (bytes.Length != 0)
            {
                // Get decoded chars from bytes
                var chars = new char[Decoder.GetCharCount(bytes, 0, received)];
                Decoder.GetChars(bytes, 0, received, chars, 0);

                // Append chars to message string builder
                MessageBuilder.Append(chars);
            }

            // Check for end of processing marker
            if (_checkForEndMarker &&
                MessageBuilder.ToString().IndexOf(EndOfProcessingMarker!) != -1)
            {
                Logger.LogTrace("End of processing marker found on iteration [{Iteration}]", iteration);

                // Return false to stop processing more
                return false;
            }

            // Return true to continue processing if there is more
            return true;
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.GetReceived"/>
        public virtual object? GetReceived()
        {
            // See if end of marker is being checked
            if (_checkForEndMarker)
            {
                // Get data and strip end of processing marker
                var str = MessageBuilder.ToString();
                return str?.TrimEnd(EndOfProcessingMarker!.ToCharArray()) ?? "";
            }

            return MessageBuilder.ToString();
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.ProcessReceived(object?)"/>
        public abstract void ProcessReceived(object? data);

        /// <inheritdoc cref="INetworkClientDataProcessor.GetAckBytes(object?)"/>
        public virtual byte[] GetAckBytes(object? data)
        {
            // Build ACK message
            var ack = BuildAckMessage(data);

            // Get bytes from ack message
            var chars = ack.ToCharArray();
            var bytes = new byte[Encoder.GetByteCount(chars, 0, chars.Length, false)];
            Encoder.GetBytes(chars, 0, chars.Length, bytes, 0, false);

            // Reset processing 
            ResetProcessing();

            return bytes;
        }

        /// <summary>
        /// Build the ACK message based on current processed message
        /// </summary>
        /// <param name="data">Data received from network</param>
        /// <returns>The ACK message to send</returns>
        protected abstract string BuildAckMessage(object? data);

        /// <summary>
        /// Reset items used to process the bytes received from the network connection
        /// </summary>
        public virtual void ResetProcessing()
        {
            MessageBuilder.Clear();
            Decoder.Reset();
            Encoder.Reset();
        }
    }
}
