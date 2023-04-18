namespace NetworkListener
{
    /// <summary>
    /// Client disconnected event args
    /// </summary>
    public class ClientDisconnectedEventArgs : ClientEventArgs
    {
        /// <summary>
        /// An exception that might have caused the disconnection
        /// </summary>
        public Exception? Exception { get; init; }
    }
}