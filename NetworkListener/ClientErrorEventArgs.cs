namespace NetworkListener
{
    /// <summary>
    /// Client error event args
    /// </summary>
    public class ClientErrorEventArgs : ClientEventArgs
    {
        /// <summary>
        /// Exception during client processing
        /// </summary>
        public Exception? Exception { get; init; }
    }
}
