namespace NetworkListener
{
    /// <summary>
    /// Client message received event args
    /// </summary>
    public class ClientMessageReceievedEventArgs : ClientEventArgs
    {
        /// <summary>
        /// The message received on the connection
        /// </summary>
        public string? Message { get; init; }
    }
}
