namespace NetworkListener
{
    /// <summary>
    /// Client data received event args
    /// </summary>
    public class ClientDataReceivedEventArgs : ClientEventArgs
    {
        /// <summary>
        /// The data received on the connection
        /// </summary>
        public object? Data { get; init; }
    }
}
