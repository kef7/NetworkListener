using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NetworkListener
{
    /// <summary>
    /// Network listener event args
    /// </summary>
    public class ListenerEventArgs : EventArgs
    {
        /// <summary>
        /// Time-stamp of the received event
        /// </summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// The listener host name
        /// </summary>
        public string? HostName { get; init; }

        /// <summary>
        /// The listener endpoint
        /// </summary>
        public EndPoint? LocalEndPoint { get; init; }
    }
}
