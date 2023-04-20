namespace NetworkListener.Builder
{
    using global::NetworkListener.NetworkCommunicationProcessors;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// A faceted builder class to build <see cref="NetworkListener"/> objects
    /// </summary>
    public class NetworkListenerBuilder
    {
        /// <summary>
        /// Internal builder class to handle required method call flow. Use in static <see cref="Create(ILogger{NetworkListener})"/>.
        /// </summary>
        private class InternalBuilder : INetworkListenerBuilderSpecifyPort, INetworkListenerBuilderSpecifyProcessor, INetworkListenerBuilderCommon
        {

            /// <summary>
            /// The <see cref="NetworkListener"/> to build
            /// </summary>
            private NetworkListener _listener;

            /// <summary>
            /// CTOR
            /// </summary>
            /// <param name="logger">Logger for the <see cref="NetworkListener"/></param>
            public InternalBuilder(ILogger<NetworkListener> logger)
            {
                _listener = new NetworkListener(logger);
            }

            /// <summary>
            /// Specify the required port number
            /// </summary>
            /// <param name="port">The port number to use</param>
            /// <returns>Ref to builder</returns>
            /// <exception cref="ArgumentOutOfRangeException">If <paramref name="port"/> is outside standard port ranges</exception>
            public INetworkListenerBuilderSpecifyProcessor UsingPort(int port)
            {
                // Validate port lower range
                if (port < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(port));
                }

                // Validate port upper range
                if (port > 65535)
                {
                    throw new ArgumentOutOfRangeException(nameof(port));
                }

                _listener.Port = port;

                return this;
            }

            /// <summary>
            /// Specify the required network communication processor object
            /// </summary>
            /// <param name="networkCommunicationProcessor">Network communication processor object for the network lister</param>
            /// <returns>Ref to builder</returns>
            /// <exception cref="ArgumentNullException">If <paramref name="networkCommunicationProcessor"/> is null</exception>
            public INetworkListenerBuilderCommon UsingProcessor(INetworkCommunicationProcessor networkCommunicationProcessor)
            {
                // Validate object
                if (networkCommunicationProcessor is null)
                {
                    throw new ArgumentNullException(nameof(networkCommunicationProcessor));
                }

                // Validate max buffer size
                if (networkCommunicationProcessor.MaxBufferSize < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(networkCommunicationProcessor.MaxBufferSize));
                }

                _listener.NetworkCommunicationProcessor = networkCommunicationProcessor;

                return this;
            }

            /// <summary>
            /// With specified IP address
            /// </summary>
            /// <param name="ipAddress">The IP address to use</param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithIPAddress(IPAddress ipAddress)
            {
                _listener.IPAddress = ipAddress;

                return this;
            }

            /// <summary>
            /// With specified socket type
            /// </summary>
            /// <param name="socketType">The socket type to use</param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithSocketType(SocketType socketType)
            {
                _listener.SocketType = socketType;

                return this;
            }

            /// <summary>
            /// With specified protocol type
            /// </summary>
            /// <param name="protocolType">The protocol type to use</param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithProtocol(ProtocolType protocolType)
            {
                _listener.ProtocolType = protocolType;

                return this;
            }

            /// <summary>
            /// With certificate and security protocol to use for secure communications
            /// </summary>
            /// <param name="certificate">The certificate to use for secure communications</param>
            /// <param name="securityProtocolType">The security protocol to use for secure communications; defaults is <see cref="SecurityProtocolType.Tls13"/></param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithCert(X509Certificate certificate, SecurityProtocolType securityProtocolType = SecurityProtocolType.Tls13)
            {
                if (certificate is null)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                _listener.Certificate = certificate;
                _listener.SecurityProtocolType = securityProtocolType;

                return this;
            }

            /// <summary>
            /// Max client connections
            /// </summary>
            /// <param name="maxClientConnections">Maximum number of connections to handle</param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithMaxClientConnections(int maxClientConnections)
            {
                // Adjust lower range
                if (maxClientConnections < 1)
                {
                    maxClientConnections = 1;
                }

                _listener.MaxClientConnections = maxClientConnections;

                return this;
            }

            /// <summary>
            /// With handling parallel connections
            /// </summary>
            /// <param name="handleParallelConnections">Flag to indicate that network listener should handle parallel connections</param>
            /// <returns>Ref to builder</returns>
            public INetworkListenerBuilderCommon WithHandleParallelConnections(bool handleParallelConnections)
            {
                _listener.HandleParallelConnections = handleParallelConnections;

                return this;
            }

            /// <summary>
            /// Build the <see cref="NetworkListener"/> object as informed
            /// </summary>
            /// <returns>Configured listener object</returns>
            public NetworkListener Build()
            {
                return _listener;
            }
        }

        /// <summary>
        /// CTOR; hide so logic will have to come from static <see cref="Create"/> method
        /// </summary>
        private NetworkListenerBuilder()
        {
        }

        /// <summary>
        /// Create new faceted builder
        /// </summary>
        /// <param name="logger">Logger for the <see cref="NetworkListener"/></param>
        /// <returns>Faceted builder for <see cref="NetworkListener"/></returns>
        public static INetworkListenerBuilderSpecifyPort Create(ILogger<NetworkListener> logger)
        {
            return new InternalBuilder(logger);
        }
    }
}
