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
        /// Default character encoding to use
        /// </summary>
        public static readonly Encoding DefaultEncoding = Encoding.UTF8;

        /// <summary>
        /// Encoding backing var for <see cref="Encoding"/>
        /// </summary>
        private Encoding _encoding = null!;

        /// <summary>
        /// Logger
        /// </summary>
        protected ILogger Logger { get; }

        /// <inheritdoc cref="INetworkClientDataProcessor.MaxBufferSize"/>
        public int MaxBufferSize { get; init; } = 4096 * 4096;

        /// <summary>
        /// Characters marker to flag the end of network bytes processing
        /// </summary>
        public virtual string? EndOfProcessingMarker { get; protected set; } = null;

        /// <summary>
        /// Check for end marker flag
        /// </summary>
        protected bool CheckForEndMarker { get; set; } = false;

        /// <summary>
        /// String builder object to hold string representation of message received from network connection
        /// </summary>
        protected virtual StringBuilder MessageBuilder { get; } = new();

        /// <summary>
        /// The character encoding for byte data received and sent on the network connection
        /// </summary>
        /// <remarks>
        /// Default encoding of <see cref="Encoding.UTF8"/> will be used when assigning this property as null
        /// </remarks>
        public virtual Encoding Encoding
        {
            get => _encoding;
            protected set
            {
                // Set to default encoding
                if (value is null)
                {
                    // Set only if not already set to default
                    if (_encoding != DefaultEncoding)
                    {
                        _encoding = DefaultEncoding;
                        Decoder = _encoding.GetDecoder();
                        Encoder = _encoding.GetEncoder();
                    }

                    // Get out
                    return;
                }

                // Set encoding and get decoder and encoder if value different
                if (_encoding != value)
                {
                    _encoding = value;
                    Decoder = _encoding.GetDecoder();
                    Encoder = _encoding.GetEncoder();
                }
            }
        }

        /// <summary>
        /// Character decoder for bytes (buffer) received from network connection
        /// </summary>
        /// <remarks>
        /// Set when <see cref="Encoding"/> is set to a concrete encoding, like <see cref="Encoding.UTF8"/>
        /// </remarks>
        protected virtual Decoder Decoder { get; private set; } = null!;

        /// <summary>
        /// Character encoder for bytes to send as acknowledgment response to bytes received from network connection
        /// </summary>
        /// <remarks>
        /// Set when <see cref="Encoding"/> is set to a concrete encoding, like <see cref="Encoding.UTF8"/>
        /// </remarks>
        protected virtual Encoder Encoder { get; private set; } = null!;

        /// <summary>
        /// CTOR for base message network client data processor; message can contain an end of 
        /// receive character to signal stopping of current network data received processing
        /// </summary>
        /// <param name="logger">Generic logger</param>
        /// <param name="encoding">Network data character encoding used to decode messages and encode bytes</param>
        /// <param name="endOfProcessingMaker">End of processing marker character; should be of the same encoding as <paramref name="encoding"/></param>
        public BaseMessageNetworkClientDataProcessor(ILogger<BaseMessageNetworkClientDataProcessor> logger, Encoding? encoding = null, string? endOfProcessingMaker = null)
        {
            Logger = logger;

            Encoding = encoding!;

            EndOfProcessingMarker = endOfProcessingMaker;
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
            if (CheckForEndMarker &&
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
            if (CheckForEndMarker)
            {
                // Get data and strip end of processing marker
                var str = MessageBuilder.ToString();
                return str?.TrimEnd(EndOfProcessingMarker!.ToCharArray()) ?? "";
            }

            return MessageBuilder.ToString();
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.ProcessData(object?)"/>
        public abstract void ProcessData(object? data);

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
            // Set check based on end of processing marker
            CheckForEndMarker = !string.IsNullOrWhiteSpace(EndOfProcessingMarker);

            // Reset message building items
            MessageBuilder.Clear();
            Decoder.Reset();
            Encoder.Reset();
        }
    }
}
