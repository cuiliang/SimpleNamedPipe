# SimpleNamedPipe
简单的NamedPipe服务器和客户端，用于文本消息的收发。

功能特点：
- 基于事件的异步消息收发。全双工通信。
- 服务端支持多客户端同时连接。
- 客户端支持一次性连接和持续长连接两种方式。
- 支持基于字节的消息传输和基于消息的消息传输。
- 支持可开启的客户端名称自动告知功能，以便区分不同的客户端。


### 一次性连接
使用`await client.ConnectAsync(int timeout);`启动连接，该方法在连接成功后返回。
```csharp
var client = new PipeClient(pipeName, useMessageBasedEncoder: useMessageEncoder);

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

### 持续长连接
```csharp
var client = new PipeClient("MyPipeName",
				clientName: "Client1");
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

## 基于字节的消息传输、基于消息的消息传输

- 基于字节的消息传输(`ByteBasedEncoder`)：消息以字节流的形式传输，消息的长度由消息头指定。
- 基于消息的消息传输(`MessageBasedEncoder`)：使用`PipeTransmissionMode.Message`模式，根据`stream.IsMessageComplete`进行判断。

默认使用基于字节的消息传输。 
可以在`PipeServer`和`PipeClient`的构造函数中指定`useMessageBasedEncoder`参数，以使用基于消息的消息传输(两端需一致)。

## 支持指定客户端名称
如果需要区分不同的客户端，可以开启客户端名称支持。

1)在`PipeServer`的构造函数中指定`enableClientName`参数，以开启客户端名称支持。
2)在`PipeClient`的构造函数中指定`clientName`参数，以便在服务端识别。
3)在`PipeServer`的`MessageReceived`事件中，可以获取到客户端名称。