namespace NetworkListener
{
    using System.Net;

    /// <summary>
    /// Client event args
    /// </summary>
    public class ClientEventArgs
    {
        /// <summary>
        /// Time-stamp of the messaged received event
        /// </summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// The remote endpoint that is connected to us
        /// </summary>
        public EndPoint? RemoteEndPoint { get; init; }
    }
}
