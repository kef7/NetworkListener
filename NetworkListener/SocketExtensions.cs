namespace NetworkListener
{
    using System.Net.Sockets;

    /// <summary>
    /// Extensions for <see cref="Socket"/> type
    /// </summary>
    public static class SocketExtensions
    {
        /// <summary>
        /// Tests if socket is connected to its remote endpoint
        /// </summary>
        /// <param name="socket">The socket to perform a connection test on</param>
        /// <param name="testBySend">Flag to indicate the connection test should be done by sending zero byte method; false by default</param>
        /// <returns>True if connected; false otherwise</returns>
        /// <remarks>
        /// By default this method attempts to poll for status. If <paramref name="testBySend"/> is true it will test by
        /// using Microsoft's zero byte method. Their zero byte method test is accomplished by sending (non-blocking) zero 
        /// byte to its current configured remote host.
        /// https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.connected?view=net-6.0
        /// </remarks>
        public static bool IsConnected(this Socket socket, bool testBySend = false)
        {
            // Check connected property
            if (!socket.Connected)
            {
                return false;
            }

            // Check if we should use the test by send method
            if (testBySend)
            {
                // Try to send zero byte; store current configured blocking state first
                bool blockingState = socket.Blocking;
                try
                {
                    // Byte array
                    var bytes = new byte[1];

                    // Set blocking to false
                    socket.Blocking = false;

                    // Send zero byte array
                    socket.Send(bytes, 0, SocketFlags.None);

                    // Returned connected property that should have the state of connected or not
                    return socket.Connected;
                }
                catch (SocketException e)
                {
                    // Check if error is WAEWOULDBLOCK value 10035; means it is still connected
                    if (e.NativeErrorCode == 10035)
                    {
                        return true;
                    }

                    // Any other socket exception we assume it is not connected
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                finally
                {
                    // Restore previous configured blocking state
                    socket.Blocking = blockingState;
                }
            }

            // Poll for status
            try
            {
                /*
                 * We poll for status but also check available to get better sense if connected.
                 * 
                 * Poll here is true when:
                 * - There is a pending connection (no connection now)
                 * - Data is available to read
                 * - Connection closed, reset, or terminated
                 */
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
