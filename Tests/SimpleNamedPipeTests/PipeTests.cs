using SimpleNamedPipe;

namespace SimpleNamedPipeTests
{
	[TestClass]
	public sealed class PipeTests
	{
		

		[TestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public async Task CanSendRecv(bool useMessageEncoder)
		{
			string pipeName = Guid.NewGuid().ToString();

			// Start server
			var server = new PipeServer(pipeName, useMessageBasedEncoder: useMessageEncoder);
			server.MessageReceived += async (sender, e) =>
			{
				await server.SendMessageAsync(e.ClientId, $"ECHO:{e.Message}");
			};
			await server.StartAsync();

			// Start client
			var client = new PipeClient(pipeName, useMessageBasedEncoder: useMessageEncoder);
			string message = "Hello, server!" + Guid.NewGuid().ToString();
			string receivedMessage = null;
			client.Connected += async (sender, args) =>
			{
				await client.SendMessageAsync(message);
			};
			client.MessageReceived += (sender, e) =>
			{
				receivedMessage = e.Message;
			};

			await client.ConnectAsync();

			await Task.Delay(1000);

			Assert.AreEqual("ECHO:" + message, receivedMessage);

			await server.StopAsync();
		}


		[TestMethod]
		public async Task SupportClientName()
		{
			string pipeName = Guid.NewGuid().ToString();
			string clientName = "MyClient";
			string receivedClientName = null;


			// Start server
			var server = new PipeServer(pipeName, enableClientName:true);
			server.MessageReceived += async (sender, e) =>
			{
				receivedClientName = e.ClientName;
				await server.SendMessageAsync(e.ClientId, $"ECHO:{e.Message}");
			};
			await server.StartAsync();

			// Start client
			
			var client = new PipeClient(pipeName, clientName: clientName);
			string message = "Hello, server!" + Guid.NewGuid().ToString();
			string receivedMessage = null;
			
			client.Connected += async (sender, args) =>
			{
				await client.SendMessageAsync(message);
			};
			client.MessageReceived += (sender, e) =>
			{
				receivedMessage = e.Message;
			};

			await client.ConnectAsync();

			await Task.Delay(1000);

			Assert.AreEqual("ECHO:" + message, receivedMessage);
			Assert.AreEqual(clientName, receivedClientName);

			await server.StopAsync();
		}

		[TestMethod]
		public async Task SupportAutoReconnect()
		{
			string pipeName = Guid.NewGuid().ToString();
			string clientName = "MyClient";
			string receivedClientName = null;


			// Start server
			var server = new PipeServer(pipeName, enableClientName: true);
			server.MessageReceived += async (sender, e) =>
			{
				receivedClientName = e.ClientName;
				await server.SendMessageAsync(e.ClientId, $"ECHO:{e.Message}");
			};
			

			// Start client

			var client = new PipeClient(pipeName, clientName: clientName);
			string message = "Hello, server!" + Guid.NewGuid().ToString();
			string receivedMessage = null;

			client.Connected += async (sender, args) =>
			{
				await client.SendMessageAsync(message);
			};
			client.MessageReceived += (sender, e) =>
			{
				receivedMessage = e.Message;
			};

			client.StartConnectWithAutoReconnection();
			await Task.Delay(100);
			Assert.IsFalse(client.IsConnected, "客户端先启动，应该无法连接。");

			await server.StartAsync();
			await Task.Delay(100);
			Assert.IsTrue(client.IsConnected, "服务器启动后，应该可以连接");

			await server.StopAsync();
			await Task.Delay(100);
			Assert.IsFalse(client.IsConnected, "服务器停止后，应该断开连接");

			await Task.Delay(100);
			await server.StartAsync();

			await Task.Delay(1000);
			Assert.IsTrue(client.IsConnected, "服务器启动后，应该重新连接");


			await server.StopAsync();
		}
	}
}
