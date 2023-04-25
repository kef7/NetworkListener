namespace NetworkListener.Builder
{
    using global::NetworkListener.NetworkClientDataProcessors;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Authentication;
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
                if (logger is null)
                {
                    throw new ArgumentNullException(nameof(logger));
                }

                _listener = new NetworkListener(logger);
            }

            /// <inheritdoc cref="INetworkListenerBuilderSpecifyPort.UsingPort(int)"/>
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

            /// <inheritdoc cref="INetworkListenerBuilderSpecifyProcessor.UsingProcessor(INetworkClientDataProcessor)"/>
            /// <exception cref="ArgumentNullException">If <paramref name="networkClientDataProcessor"/> is null</exception>
            public INetworkListenerBuilderCommon UsingProcessor(INetworkClientDataProcessor networkClientDataProcessor)
            {
                // Validate object
                if (networkClientDataProcessor is null)
                {
                    throw new ArgumentNullException(nameof(networkClientDataProcessor));
                }

                // Validate max buffer size
                if (networkClientDataProcessor.MaxBufferSize < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(networkClientDataProcessor.MaxBufferSize));
                }

                _listener.NetworkClientDataProcessor = networkClientDataProcessor;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithHostName(string)"/>
            public INetworkListenerBuilderCommon WithHostName(string hostName)
            {
                _listener.HostName = hostName;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithIPAddress(IPAddress)"/>
            public INetworkListenerBuilderCommon WithIPAddress(IPAddress ipAddress)
            {
                if (ipAddress is null)
                {
                    throw new ArgumentNullException(nameof(ipAddress));
                }

                _listener.IPAddress = ipAddress;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithSocketType(SocketType)"/>
            public INetworkListenerBuilderCommon WithSocketType(SocketType socketType)
            {
                _listener.SocketType = socketType;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithProtocol(ProtocolType)"/>
            public INetworkListenerBuilderCommon WithProtocol(ProtocolType protocolType)
            {
                _listener.ProtocolType = protocolType;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithCert(X509Certificate, SslProtocols?)"/>
            public INetworkListenerBuilderCommon WithCert(X509Certificate certificate, SslProtocols? sslProtocols = null)
            {
                if (certificate is null)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                _listener.Certificate = certificate;
                _listener.SslProtocols = sslProtocols;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithMaxClientConnections(int)"/>
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

            /// <inheritdoc cref="INetworkListenerBuilderCommon.WithHandleParallelConnections(bool)"/>
            public INetworkListenerBuilderCommon WithHandleParallelConnections(bool handleParallelConnections)
            {
                _listener.HandleParallelConnections = handleParallelConnections;

                return this;
            }

            /// <inheritdoc cref="INetworkListenerBuilderCommon.Build"/>
            public NetworkListener Build()
            {
                return _listener;
            }
        }

        /// <summary>
        /// CTOR; hide so logic will have to come from static <see cref="Create(ILogger{NetworkListener})"/> 
        /// or <see cref="Create()"/> method
        /// </summary>
        private NetworkListenerBuilder()
        {
        }

        /// <summary>
        /// Create new faceted builder for building a <see cref="NetworkListener"/>
        /// </summary>
        /// <param name="logger">Logger for the <see cref="NetworkListener"/></param>
        /// <returns>Faceted builder for <see cref="NetworkListener"/></returns>
        public static INetworkListenerBuilderSpecifyPort Create(ILogger<NetworkListener> logger)
        {
            return new InternalBuilder(logger);
        }

        /// <summary>
        /// Create new faceted builder for building a <see cref="NetworkListener"/>
        /// </summary>
        /// <returns>Faceted builder for <see cref="NetworkListener"/></returns>
        public static INetworkListenerBuilderSpecifyPort Create()
        {
            var logger = NullLoggerFactory.Instance.CreateLogger<NetworkListener>();
            return new InternalBuilder(logger);
        }
    }
}
