

using SimpleNamedPipe.Server;

namespace NamedPipeLite
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
			var server = new PipeServer("MyPipeName", true,
				useMessageBasedEncoder:false);

			// 注册事件处理
			server.ClientConnected += (sender, e) =>
				Console.WriteLine($"Client {e.ClientId} connected");

			server.ClientDisconnected += (sender, e) =>
				Console.WriteLine($"Client {e.ClientId} disconnected");

			server.MessageReceived += async (sender, e) =>
			{
				var msg = e.Message.Length > 20 ? e.Message.Substring(0, 20) + "..." : e.Message;

				Console.WriteLine($"Received from client {e.ClientId}|{e.ClientName}: {e.Message.Length} {msg}...");
				// 可以在这里处理接收到的JSON消息
				try
				{
					// 示例：发送响应
					await server.SendMessageAsync(e.ClientId, $"Echo: {e.Message}");

				}
				catch (Exception ex)
				{
					Console.WriteLine("发送出错：" + ex.Message);
				}
			};
			// 启动服务器
			await server.StartAsync();

			// 等待用户输入来停止服务器
			Console.WriteLine("Press Enter to stop server");
			Console.ReadLine();

			// 停止服务器
			await server.StopAsync();

			//         var namedPipeServer = new NamedPipeServer("test");
			//namedPipeServer.ClientConnected += (sender, e) =>
			//{
			//	Console.WriteLine($"Client connected: {e.ClientId}");
			//};
			//namedPipeServer.ClientDisconnected += (sender, e) =>
			//{
			//	Console.WriteLine($"Client disconnected: {e.ClientId}");
			//};
			//namedPipeServer.MessageReceived += (sender, e) =>
			//{
			//	Console.WriteLine($"Message received from {e.ClientId}: {e.Message}");
			//};
			////Console.WriteLine("Press any key to Start...");
			////Console.ReadKey();
			//namedPipeServer.Start();

			//Console.WriteLine("Press any key to Stop...");
			//Console.ReadKey();

			//namedPipeServer.Stop();

			Console.WriteLine("Press any key to Exit...");
			Console.ReadKey();
		}
    }
}
