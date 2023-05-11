namespace NetworkListenerCore
{
    internal interface IClientEvents
    {
        /// <summary>
        /// Client connected event signature
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected;

        /// <summary>
        /// Client data received event signature
        /// </summary>
        public event EventHandler<ClientDataReceivedEventArgs>? ClientDataReceived;

        /// <summary>
        /// Client disconnected event signature
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

        /// <summary>
        /// Client error event signature
        /// </summary>
        public event EventHandler<ClientErrorEventArgs>? ClientError;
    }
}
