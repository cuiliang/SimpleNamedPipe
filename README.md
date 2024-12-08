# SimpleNamedPipe
�򵥵�NamedPipe�������Ϳͻ��ˣ������ı���Ϣ���շ���

�����ص㣺
- �����¼����첽��Ϣ�շ���ȫ˫��ͨ�š�
- �����֧�ֶ�ͻ���ͬʱ���ӡ�
- �ͻ���֧��һ�������Ӻͳ������������ַ�ʽ��
- ֧�ֻ����ֽڵ���Ϣ����ͻ�����Ϣ����Ϣ���䡣
- ֧�ֿɿ����Ŀͻ��������Զ���֪���ܣ��Ա����ֲ�ͬ�Ŀͻ��ˡ�


### һ��������
ʹ��`await client.ConnectAsync(int timeout);`�������ӣ��÷��������ӳɹ��󷵻ء�
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

### ����������
```csharp
var client = new PipeClient("MyPipeName",
				clientName: "Client1");
// ע���¼�������
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


// ���ӵ�������
client.StartConnectWithAutoReconnection();
```

## �����ֽڵ���Ϣ���䡢������Ϣ����Ϣ����

- �����ֽڵ���Ϣ����(`ByteBasedEncoder`)����Ϣ���ֽ�������ʽ���䣬��Ϣ�ĳ�������Ϣͷָ����
- ������Ϣ����Ϣ����(`MessageBasedEncoder`)��ʹ��`PipeTransmissionMode.Message`ģʽ������`stream.IsMessageComplete`�����жϡ�

Ĭ��ʹ�û����ֽڵ���Ϣ���䡣 
������`PipeServer`��`PipeClient`�Ĺ��캯����ָ��`useMessageBasedEncoder`��������ʹ�û�����Ϣ����Ϣ����(������һ��)��

## ֧��ָ���ͻ�������
�����Ҫ���ֲ�ͬ�Ŀͻ��ˣ����Կ����ͻ�������֧�֡�

1)��`PipeServer`�Ĺ��캯����ָ��`enableClientName`�������Կ����ͻ�������֧�֡�
2)��`PipeClient`�Ĺ��캯����ָ��`clientName`�������Ա��ڷ����ʶ��
3)��`PipeServer`��`MessageReceived`�¼��У����Ի�ȡ���ͻ������ơ�