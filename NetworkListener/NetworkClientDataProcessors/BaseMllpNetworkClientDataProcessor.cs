using Microsoft.Extensions.Logging;
using System.Text;

namespace NetworkListener.NetworkClientDataProcessors
{
    /// <summary>
    /// Base minimal lower layer protocol (MLLP) network client data processor
    /// </summary>
    public abstract class BaseMllpNetworkClientDataProcessor : BaseMessageNetworkClientDataProcessor
    {
        /// <summary>
        /// MLLP start block character; signals starting of MLLP wrapped message;
        /// default is ASCII (11) vertical tab
        /// </summary>
        public virtual char MllpStartChar { get; init; } = (char)11;

        /// <summary>
        /// MLLP separator character; signals line/block separation;
        /// default is ASCII (13) carriage return
        /// </summary>
        public virtual char MllpSeparatorChar { get; init; } = (char)13;

        /// <summary>
        /// MLLP end block character; signals end of MLLP wrapped message;
        /// default is ASCII (28) file separator
        /// </summary>
        public virtual char MllpEndChar { get; init; } = (char)28;

        /// <summary>
        /// New line character of the same character encoding as <see cref="Decoder"/> 
        /// and <see cref="Encoder"/>
        /// </summary>
        public virtual char? NewLine { get; init; } = null;

        /// <summary>
        /// CTOR for base MLLP network client data processor
        /// </summary>
        /// <param name="logger">Generic logger</param>
        /// <param name="encoding">Network data character encoding used to decode messages and encode bytes</param>
        public BaseMllpNetworkClientDataProcessor(ILogger<BaseMllpNetworkClientDataProcessor> logger, Encoding? encoding)
            : base(logger, encoding)
        {
            // Set to MLLP end character so processing will end if needed
            EndOfProcessingMarker = MllpEndChar.ToString();

            // Set encoding new line character
            try
            {
                if (!NewLine.HasValue)
                {
                    NewLine = Encoding.GetString(new byte[] { (byte)'\n' }).ToCharArray()[0];
                }
            }
            catch
            {
                NewLine = null;
            }
        }

        /// <inheritdoc cref="BaseMessageNetworkClientDataProcessor.GetReceived"/>
        public override object? GetReceived()
        {
            // Parse message body out of MLLP message
            var mllpMsg = base.GetReceived() as string;
            var message = ParseMllpMessage(mllpMsg);

            // Replace MLLP separator characters with the encoding new line character
            if (NewLine.HasValue)
            {
                message = message.Replace(MllpSeparatorChar, NewLine.Value);
            }

            return message;
        }

        /// <summary>
        /// Parse inner message out of MLLP wrapped message
        /// </summary>
        /// <param name="message">Full MLLP wrapped message string</param>
        /// <param name="defaultValue">Default value to return if invalid MLLP format found</param>
        /// <returns>String of the message that was wrapped in the MLLP block</returns>
        public virtual string ParseMllpMessage(string? message, string? defaultValue = "")
        {
            Logger.LogTrace("Parsing MLLP message");

            if (string.IsNullOrWhiteSpace(message))
            {
                return defaultValue ?? "";
            }

            // Look for MLLP start block
            var sbIndex = message.IndexOf(MllpStartChar);
            if (sbIndex >= 0)
            {
                // Look for MLLP end block
                var ebIndex = message.IndexOf(MllpEndChar);

                // Check if end is larger or equal to start
                if (ebIndex > sbIndex)
                {
                    // Calculate what to strip out
                    var startIndex = sbIndex + 1;       // Next char
                    var length = ebIndex - sbIndex - 1; // End minus start minus next last char
                    if (length > 0)
                    {
                        // Strip out message
                        var msg = message.Substring(startIndex, length);

                        Logger.LogTrace("MLLP message parsed");

                        // Replace MLLP segment separator on the end
                        return msg.TrimEnd(MllpSeparatorChar);
                    }
                }
            }

            return defaultValue ?? "";
        }

        /// <summary>
        /// Build MLLP wrapped message
        /// </summary>
        /// <param name="messageSegments">Segments of the message to be wrapped; usually line content of the message</param>
        /// <returns>Wrapped MLLP message</returns>
        /// <remarks>
        /// Can be used in <see cref="BaseMessageNetworkClientDataProcessor.GetAckBytes"/> 
        /// for building proper MLLP wrapped message for acknowledgment.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="messageSegments"/> is null</exception>
        public virtual string BuildMllpMessage(params string[] messageSegments)
        {
            if (messageSegments is null)
            {
                throw new ArgumentNullException(nameof(messageSegments));
            }

            // Build message
            var msg = string.Join(MllpSeparatorChar, messageSegments);

            // Wrap as MLLP message
            var sb = new StringBuilder();
            sb.Append(MllpStartChar)
                .Append(msg)
                .Append(MllpSeparatorChar)
                .Append(MllpEndChar)
                .Append(MllpSeparatorChar);

            return sb.ToString();
        }
    }
}
