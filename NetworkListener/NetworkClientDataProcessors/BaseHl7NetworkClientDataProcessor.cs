using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetworkListener.NetworkClientDataProcessors
{
    /// <summary>
    /// Base HL7 network client data processor; based on MLLP processor <see cref="BaseMllpNetworkClientDataProcessor"></see>
    /// </summary>
    public abstract class BaseHl7NetworkClientDataProcessor : BaseMllpNetworkClientDataProcessor
    {
        /// <summary>
        /// HL7 version; used for message validation and building
        /// </summary>
        public virtual string Hl7Version { get; init; }

        /// <summary>
        /// CTOR for HL7 network client data processor
        /// </summary>
        /// <param name="logger">Generic logger</param>
        public BaseHl7NetworkClientDataProcessor(ILogger<BaseHl7NetworkClientDataProcessor> logger)
            : base(logger)
        {
            // Force HL7 version check
            if (string.IsNullOrWhiteSpace(Hl7Version))
            {
                Hl7Version = "2.9";
            }
        }

        /// <inheritdoc cref="BaseMllpNetworkClientDataProcessor.BuildAckMessage(object?)"/>
        protected override string BuildAckMessage(object? data)
        {
            // Get message- which is the HL7 message that
            // is the inner part of the MLLP message block
            var hl7Msg = data as string;

            // Build HL7 ACK
            var hl7Ack = BuildHl7AcknowledgementMessage(hl7Msg!);

            return hl7Ack;
        }

        /// <summary>
        /// Build HL7 ACK message from HL7 message received
        /// </summary>
        /// <param name="hl7Message">HL7 message received</param>
        /// <returns>Valid HL7 ACK/NACK message</returns>
        /// <remarks>
        /// Base class method checks the HL7 message for a value that is not null/empty/while-space, 
        /// then parses out the HL7 message control identifier and uses it when it
        /// builds a standard HL7 positive acknowledgment message.
        /// </remarks>
        public virtual string BuildHl7AcknowledgementMessage(string hl7Message)
        {
            if (string.IsNullOrWhiteSpace(hl7Message))
            {
                // Build and return NACK message
                return "";
            }

            // Get message control ID
            var msgCtrlId = hl7Message
                .Split(MllpSeparatorChar)?[0]  // Message header, "MSH..?"
                .Split('|')?[9];            // Field 10, "MSG00000..?"

            // Build ACK message
            var sb = new StringBuilder();
            sb.Append("MSH|^~\\&|||||||ACK||P|")
                .Append(Hl7Version)
                .Append(MllpSeparatorChar)
                .Append("MSA|AA|")
                .Append(msgCtrlId);

            return sb.ToString();
        }
    }
}
