# SimpleNamedPipe
简单的NamedPipe服务器和客户端，用于文本消息的收发。

功能特点：
- 基于事件的异步消息收发。全双工通信。
- 服务端支持多客户端同时连接。
- 客户端支持一次性连接和持续长连接两种方式。
- 支持多种消息传输模式（基于字节流、基于消息、兼容BinaryFormatter）。
- 支持可开启的客户端名称自动告知功能，以便区分不同的客户端。


### 一次性连接
使用`await client.ConnectAsync(int timeout);`启动连接，该方法在连接成功后返回。
```csharp
var client = new PipeClient(pipeName);

client.Connected += async (sender, args) =>
{
	await client.SendMessageAsync(message);
};
client.MessageReceived += (sender, e) =>
{
	receivedMessage = e.Message;
};

await client.ConnectAsync();
```
> [!NOTE]
> `PipeClient` 的构造函数已更新。请参考下面的“持续长连接”部分查看如何使用 `MessageTransmissionMode` 来实例化客户端。

### 持续长连接
```csharp
var client = new PipeClient("MyPipeName",
				clientName: "Client1",
                transmissionMode: MessageTransmissionMode.ByteBasedBigEndian);
// 注册事件处理器
client.Connected += async (sender, e) =>
{
	Console.WriteLine($"Connected: {e.Message}");

	await client.SendMessageAsync("Hello Server!");
};

client.Disconnected += (sender, e) =>
	Console.WriteLine($"Disconnected: {e.Message}");

client.Reconnecting += (s, e) => Console.WriteLine($"Reconnecting: {e.Message}");


client.MessageReceived += (sender, e) =>
{
	//var msg = e.Message.Length > 30 ? e.Message.Substring(0, 30) + "..." : e.Message;

	//Console.WriteLine($"Received: {e.Message.Length} {msg}...");
	if (e.Message.Length > 30)
	{
		Console.Write($".");
	}
	else
	{
		Console.WriteLine($"Received: {e.Message}");
	}
};

client.Error += (sender, e) =>
	Console.WriteLine($"Error: {e.Message}, Exception: {e.Exception}");


// 连接到服务器
client.StartConnectWithAutoReconnection();
```

### 服务端示例

```csharp
var server = new PipeServer("MyPipeName", 
    enableClientName: true, 
    transmissionMode: MessageTransmissionMode.ByteBasedBigEndian);

server.ClientConnected += (s, args) => 
    Console.WriteLine($"Client connected: ClientId={args.ClientInfo.ClientId}, Name={args.ClientInfo.ClientName}");

server.ClientDisconnected += (s, args) => 
    Console.WriteLine($"Client disconnected: ClientId={args.ClientInfo.ClientId}");

server.MessageReceived += (s, args) =>
{
    Console.WriteLine($"Message from {args.ClientInfo.ClientName ?? args.ClientInfo.ClientId.ToString()}: {args.Message}");
    // 广播给所有客户端
    _ = server.BroadcastMessageAsync($"Echo from server: {args.Message}");
};

await server.StartAsync();

Console.WriteLine("Server started. Press Enter to exit.");
Console.ReadLine();

await server.StopAsync();
```

## 消息传输模式 (Message Transmission Modes)

 使用 `MessageTransmissionMode` 支持更灵活的消息编码和传输方式。您可以在 `PipeServer` 和 `PipeClient` 的构造函数中指定此模式，**两端的设置必须一致**。

```csharp
public enum MessageTransmissionMode
{
    // 基于消息的传输模式（仅限Windows）
    MessageBased,
    
    // 基于字节流的传输模式，使用小端字节序存储消息长度
    ByteBasedLittleEndian,
    
    // 基于字节流的传输模式，使用大端字节序存储消息长度（默认）
    ByteBasedBigEndian,
    
    // 兼容旧版BinaryFormatter的传输模式，使用小端字节序
    BinaryFormatterCompatibleLittleEndian,
    
    // 兼容旧版BinaryFormatter的传输模式，使用大端字节序
    BinaryFormatterCompatibleBigEndian
}
```

- `MessageBased`: 使用 Windows 原生的 `PipeTransmissionMode.Message` 模式。这种模式下，系统保证每次读取操作返回一个完整的消息。**此模式仅在 Windows 上可用**。
- `ByteBasedBigEndian` / `ByteBasedLittleEndian`: 使用基于字节流的传输。每条消息前会附加一个4字节的整数（分别为大端或小端字节序）来表示消息体的长度。这是跨平台的推荐方式。`ByteBasedBigEndian` 是默认选项。
- `BinaryFormatterCompatibleBigEndian` / `BinaryFormatterCompatibleLittleEndian`: 用于和使用 .NET Framework 的 `BinaryFormatter` 序列化字符串的旧版应用进行通信。它在内部模拟了 `BinaryFormatter` 对字符串的序列化格式，并同样带有4字节的长度前缀。

## 支持指定客户端名称
如果需要区分不同的客户端，可以开启客户端名称支持。

1) 在`PipeServer`的构造函数中指定`enableClientName: true`参数，以开启客户端名称支持。
2) 在`PipeClient`的构造函数中指定`clientName`参数，以便在服务端识别。
3) 在`PipeServer`的`MessageReceived`、`ClientConnected`等事件中，可以从`ClientInfo`对象中获取到客户端名称。

```csharp
// 服务端
var server = new PipeServer("MyPipe", enableClientName: true);
server.MessageReceived += (s, args) =>
{
    Console.WriteLine($"Message from client {args.ClientInfo.ClientName} (ID: {args.ClientInfo.ClientId}): {args.Message}");
};

// 客户端
var client = new PipeClient("MyPipe", clientName: "MyClient1");
```