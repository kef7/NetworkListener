# Network Listener

Multithreaded network client listener with injectable client processing and support for secured communications over SSL/TLS.

## NetworkListener Class

The **NetworkListener** class is the main component of this library. It handles the initialization of the listener, client threads, processing of client data over the network, and logging using ***Microsoft.Extensions.Logging***. Details of how client data is handled is in the injectable **INetworkClientDataProcessor** implemented object you provide.

### Using The Listener

After building a new listener instance, see [NetworkListenerBuilder section](#networklistenerbuilder-class), call the Listen() method to start the listening for and processing client connections.

```c#
using var netListener = BuildNetworkListener();
netListener.Listen();
```

You call also pass in a standard ***System.Threading.CancellationToken*** to allow for cancelling the listener and all client processing.

```c#
var cts = new CancellationTokenSource();

//...

using var netListener = BuildNetworkListener();
netListener.Listen(cts.Token);
```

## NetworkListenerBuilder Class

The library comes with a builder class that will assist you in building a **NetworkListener** instance called, **NetworkListenerBuilder**. Call the Create() method to get a new instance of the builder itself, see below:

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

The port to listen on and a factory function to generate your network client data processor is required before Build() can be called. The factory function is called for every client that connects to provide the client thread with the appropriate client data processor instance to process the client's data.

## INetworkClientDataProcessor Interface

To use **NetworkListener** you must first inject an object that implements **INetworkClientDataProcessor**. This defines the standard interfaces that **NetworkListener** class will call to processes client data.

The **INetworkClientDataProcessor** interface and the order that **NetworkListener** uses it are below:

| Interface | Order | Description |
|-|-|-|
| **int MaxBufferSize { get; }** |  | Max buffer size for byte arrays and received data |
| **void Initialize(EndPoint remoteEndPoint);** | 1 | Initialize the network client data processor. This is called before the listener starts processing client data. |
| **bool ReceiveBytes(in byte[] bytes, in int received, in int iteration);** | 2 ... | Receive bytes from the client socket. Called until no more bytes are available to process. |
| **object? GetData();** | 3 | Get data from all received bytes gathered during previous step. Compile all the bytes you received prior here. |
| **void ProcessData(object? data);** | 4 | Process the data received from the client. |
| **bool SendBytes(out byte[] bytes, in int sent, in int iteration);** | 5, back to 2 | Send bytes to the client socket. This is where you respond to the client if needed- send null or 0 length array to not send a response. |

There are several abstract classes, which implment **INetworkClientDataProcessor**, that define basic ways of processing client data:

- **MessageNetworkClientDataProcessor** - Simple message which may contain end of read flags/marker.
- **MllpNetworkClientDataProcessor** - Minimal Lower Layer Protocol, which is a common method of network communication that wrap a messages; derrives from **MessageNetworkClientDataProcessor**

## NetworkListener Events

The **NetworkListener** class has several events that can be utilized.

| Event Name | Description |
|-|-|
| **ClientConnected** | Event that is triggered when a client connects |
| **ClientDataReceived** | Event that is triggered when data is received from the client |
| **ClientDisconnected** | Event that is triggered when a client disconnects |
| **ClientError** | Event that is triggered on a client processing error |
| **Started** | Event that is triggered when the listener starts |
| **Stopped** | Event that is triggered when the listener stops |

## SSL/TLS Support

To handle secured communications you must provide a host name and a certificate that contains the host name (other configurations like DNS, or host name with IP use, is beyond this document). You provide the **NetworkListener** object this information with the builder, see example below.

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
var cert = LoadX509Certificate();
if (cert is not null)
{
    netListenerBuilder.WithCert(cert);
    netListenerBuilder.WithHostName("test.my-host.com");
}

// Build listener
var netListener = netListenerBuilder
    .WithSocketType(System.Net.Sockets.SocketType.Stream)
    .WithProtocol(System.Net.Sockets.ProtocolType.Tcp)
    .WithMaxClientConnections(1000)
    .Build();
```
