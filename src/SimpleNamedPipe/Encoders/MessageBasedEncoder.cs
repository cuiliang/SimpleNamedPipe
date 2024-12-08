using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleNamedPipe;

/// <summary>
/// 基于 PipeTransmissionMode.Message 的消息编码器（不进行什么编码，直接发送字符串）
/// </summary>
internal class MessageBasedEncoder : IMessageEncoder
{
	/// <summary>
	/// 对应的管道传输模式
	/// </summary>
#pragma warning disable CA1416
	public PipeTransmissionMode TransmissionMode => PipeTransmissionMode.Message;
#pragma warning restore CA1416

	public async Task WriteMessageAsync(PipeStream stream, string message)
	{
		byte[] buffer = Encoding.UTF8.GetBytes(message);
		await stream.WriteAsync(buffer, 0, buffer.Length);
		await stream.FlushAsync();
	}


	public async Task<string> ReadMessageAsync(PipeStream stream, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[4096];
		var messageBuilder = new StringBuilder();

		do
		{
			int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
			if (bytesRead == 0) 
				break; // 连接已关闭

			messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
		}
		while (!stream.IsMessageComplete);

		string message = messageBuilder.ToString();

		return message;
	}
}