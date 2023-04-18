namespace NetworkListener
{
    using System.Net;

    /// <summary>
    /// Network listener received event args
    /// </summary>
    public class NetworkListenerReceiveEventArgs : EventArgs
    {
        /// <summary>
        /// Time-stamp of the messaged that was received
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The remote endpoint that is connected to us
        /// </summary>
        public EndPoint? RemoteEndPoint { get; init; }

        /// <summary>
        /// The message received on the connection
        /// </summary>
        public string? Message { get; init; }
    }
}
