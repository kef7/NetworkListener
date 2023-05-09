# Network Listener

Multithreaded network client listener with injectable client processing that also supports secured communications over SSL/TLS.

## NetworkListener Class

The *NetworkListener* class is the main component of this library. It handles the initialization of the listener, client threads, processing of client data over the network, and logging using *Microsoft.Extensions.Logging*. Details of how client data is handled is in the injectable *INetworkClientDataProcessor* implemented object you provide.

### Using The Listener

After building a new listener instance call the Listen() method to start the listening for and processing client connections. This call will block the current thread.

```c#
using var netListener = BuildNetworkListener();
netListener.Listen();
```

You call also pass in a standard .NET *CancellationToken* to allow for cancelling the listener and all client processing.

```c#
var cts = new CancellationTokenSource();

//...

using var netListener = BuildNetworkListener();
netListener.Listen(cts.Token);
```

### NetworkListener Events

The *NetworkListener* class has several events that can be utilized.

| Event Name | Description |
|-|-|
| ClientConnected | Event that is triggered when a client connects |
| ClientDataReceived | Event that is triggered when data is received from the client |
| ClientDisconnected | Event that is triggered when a client disconnects |
| ClientError | Event that is triggered on a client processing error |
| ClientWaiting | Event that is triggered when client processing is in a waiting state (no data is being received but the client is still connected) |
| Started | Event that is triggered when the listener starts |
| Stopped | Event that is triggered when the listener stops |

## INetworkClientDataProcessor Interface

To use *NetworkListener* you must first inject an object that implements *INetworkClientDataProcessor*. There are several abstract classes that are simple implementations that you can also use, *MessageNetworkClientDataProcessor* and *MllpNetworkClientDataProcessor*.

The *INetworkClientDataProcessor* interface and the order that *NetworkListener* uses it are below:

| Interface | Order | Description |
|-|-|-|
| int MaxBufferSize { get; } |  | Max buffer size for byte arrays and received data |
| void Initialize(EndPoint remoteEndPoint); | 1 | Initialize the network client data processor. This is called before the listener starts processing client data. |
| bool ReceiveBytes(in byte[] bytes, in int received, in int iteration); | 2 ... | Receive bytes from the client socket. Called until no more bytes are available to process. |
| object? GetData(); | 3 | Get data from all received bytes gathered during previous step. Compile all the bytes you received prior here. |
| void ProcessData(object? data); | 4 | Process the data received from the client. |
| bool SendBytes(out byte[] bytes, in int sent, in int iteration); | 5, back to 2 | Send bytes to the client socket. This is where you respond to the client if needed- send null or 0 length array to not send a response. |

## NetworkListenerBuilder Class

The library comes with a builder class that will assist you in building a *NetworkListener* instance called, *NetworkListenerBuilder*. Call the Create() method to get a new instance of the builder itself, see below:

```c#
// Create network listener builder; you can inject logging here in the Create() static method
var netListenerBuilder = NetworkListenerBuilder.Create()
    .UsingPort(9373)
    .UsingNcdpFactory(() =>
    {
        // Custom simple client data processor based on MessageNetworkClientDataProcessor
        return new SimpleMessageNetworkClientDataProcessor();
    });

// Build listener; default streaming TCP listener 
var netListener = netListenerBuilder.Build();
```

## SSL/TLS Support

To handle secured communications you must provide a host name and a certificate that contains the hostname (other configurations like DNS, or host name with IP use, is beyond this document). You provide the *NetworkListener* object this information with the builder, see below.

```c#
// Create network listener builder; you can inject logging here int he Create() static method
var netListenerBuilder = NetworkListenerBuilder.Create()
    .UsingPort(9373)
    .UsingNcdpFactory(() =>
    {
        // Custom simple client data processor based on MessageNetworkClientDataProcessor
        return new SimpleMessageNetworkClientDataProcessor();
    });

// Load cert and provide to builder; will use host system SSL/TLS versions and cyphers
var cert = LoadCertificate();
if (cert is not null)
{
    netListenerBuilder.WithCert(cert);
    netListenerBuilder.WithHostName("test.my-host.com");
}

// Build listener
var netListener = netListenerBuilder
    .WithSocketType(System.Net.Sockets.SocketType.Stream)
    .WithProtocol(System.Net.Sockets.ProtocolType.Tcp)
    .WithHandleParallelConnections(true)
    .WithMaxClientConnections(1000)
    .Build();
```