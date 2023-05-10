using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace NetworkListenerCore.NetworkClientDataProcessors
{
    /// <summary>
    /// Network client data processor for simple messages that contain an end of message processing marker and ACK
    /// </summary>
    public abstract class MessageNetworkClientDataProcessor : INetworkClientDataProcessor
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
        /// The client remote endpoint
        /// </summary>
        public EndPoint? RemoteEndPoint { get; set; } = null;

        /// <summary>
        /// Characters marker to flag the end of network bytes processing
        /// </summary>
        public virtual string? EndOfProcessingMarker { get; protected set; } = null;

        /// <summary>
        /// Check for end marker flag
        /// </summary>
        protected bool CheckForEndMarker { get; set; } = false;

        /// <summary>
        /// Flag to inform processing to strip end marker before processing message
        /// </summary>
        public bool StripEndMarker { get; set; } = true;

        /// <summary>
        /// String builder object to hold string representation of message received from network connection
        /// </summary>
        protected virtual StringBuilder MessageBuilder { get; set; } = new();

        /// <summary>
        /// Message received from network connection
        /// </summary>
        protected virtual string? Message { get; set; } = null;

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
        public MessageNetworkClientDataProcessor(ILogger<MessageNetworkClientDataProcessor> logger, Encoding? encoding = null, string? endOfProcessingMaker = null)
        {
            Logger = logger;

            Encoding = encoding!;

            EndOfProcessingMarker = endOfProcessingMaker;
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.Initialize(EndPoint)"/>
        public virtual void Initialize(EndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;

            // Set check based on end of processing marker
            CheckForEndMarker = !string.IsNullOrWhiteSpace(EndOfProcessingMarker);

            ResetProcessing();
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.ReceiveBytes(in byte[], in int, in int)"/>
        public virtual bool ReceiveBytes(in byte[] bytes, in int received, in int iteration)
        {
            Log(LogLevel.Trace, "Processing [{BytesReceived}] bytes received on iteration [{Iteration}]", received, iteration);

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
                Log(LogLevel.Trace, "End of processing marker found on iteration [{Iteration}]", iteration);

                // Strip end of processing marker if needed
                if (StripEndMarker)
                {
                    MessageBuilder = MessageBuilder.Replace(EndOfProcessingMarker!, "");
                }

                // Return false to stop processing more
                return false;
            }

            // Return true to continue processing if there is more
            return true;
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.GetData"/>
        public virtual object? GetData()
        {
            // Return previous message
            if (Message is not null)
            {
                return Message;
            }

            // Get message from builder
            Message = MessageBuilder.ToString();

            return Message;
        }

        /// <inheritdoc cref="INetworkClientDataProcessor.ProcessData(object?)"/>
        public abstract void ProcessData(object? data);

        /// <inheritdoc cref="INetworkClientDataProcessor.SendBytes(out byte[], in int, in int)"/>
        public virtual bool SendBytes(out byte[] bytes, in int sent, in int iteration)
        {
            // Build ACK message
            var ack = BuildAckMessage();

            // Get bytes from ack message
            var chars = ack.ToCharArray();
            bytes = new byte[Encoder.GetByteCount(chars, 0, chars.Length, false)];
            Encoder.GetBytes(chars, 0, chars.Length, bytes, 0, false);

            // Reset processing 
            ResetProcessing();

            return false;
        }

        /// <summary>
        /// Build the ACK message based on current processed message
        /// </summary>
        /// <returns>The ACK message to send</returns>
        protected abstract string BuildAckMessage();

        /// <summary>
        /// Reset items used to process the bytes received from the network connection
        /// </summary>
        public virtual void ResetProcessing()
        {
            // Reset message building items
            MessageBuilder.Clear();
            Message = null;
            Decoder.Reset();
            Encoder.Reset();
        }

        /// <summary>
        /// Log with default args
        /// </summary>
        /// <param name="logLevel">Logging level</param>
        /// <param name="exception">The exception to log</param>
        /// <param name="message">Message to log</param>
        /// <param name="args">Message args to log</param>
        protected virtual void Log(LogLevel logLevel, Exception? exception, string? message, params object?[] args)
        {
            if (string.IsNullOrWhiteSpace(message) ||
                Logger is null)
            {
                return;
            }

            // Create message with default args mappings like {{NamedItem}}
            var msg = $"Client [{{ClientRemoteEndPoint}}] - {message}";

            // Create args array for all args, default and passed in
            const int defaultArgsCnt = 1;
            var cnt = args?.Length + defaultArgsCnt ?? defaultArgsCnt;
            var allArgs = new object?[cnt];

            // Assign our default args
            // If more here, increase defaultArgsCnt assign values above and
            // add them to msg like {{NamedItem}}
            allArgs[0] = RemoteEndPoint;

            // Copy over passed in args if needed
            args?.CopyTo(allArgs, 1);

            // Log message to trace
            Logger.Log(logLevel, exception, msg, allArgs);
        }

        /// <summary>
        /// Log with default args
        /// </summary>
        /// <param name="logLevel">Logging level</param>
        /// <param name="message">Message to log</param>
        /// <param name="args">Message args to log</param>
        protected virtual void Log(LogLevel logLevel, string? message, params object?[] args)
        {
            Log(logLevel, null, message, args);
        }
    }
}
