using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Buffers;

namespace SimpleNamedPipe;

/// <summary>
/// 基于 PipeTransmissionMode.Message 的消息编码器（不进行什么编码，直接发送字符串）
/// </summary>
internal class MessageBasedEncoder : IMessageEncoder
{
	/// <summary>
	/// 对应的管道传输模式
	/// </summary>
	// CA1416: This type is only available on 'windows' 7.0 and later. PipeTransmissionMode.Message is Windows-specific.
	// Using this encoder will limit the pipe's cross-platform compatibility.
#pragma warning disable CA1416
	public PipeTransmissionMode TransmissionMode => PipeTransmissionMode.Message;
#pragma warning restore CA1416

	public async Task WriteMessageAsync(PipeStream stream, string message, CancellationToken cancellationToken)
	{
		byte[] buffer = Encoding.UTF8.GetBytes(message);
		await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}


	public async Task<string> ReadMessageAsync(PipeStream stream, CancellationToken cancellationToken)
	{
		byte[] buffer = ArrayPool<byte>.Shared.Rent(4096); // Rent buffer from pool
		var messageBuilder = new StringBuilder();

		try
		{
			do
			{
				int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
				if (bytesRead == 0)
					break; // 连接已关闭

				messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
			}
			while (!stream.IsMessageComplete);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer); // Return buffer to pool
		}
		
		string message = messageBuilder.ToString();

		return message;
	}
}