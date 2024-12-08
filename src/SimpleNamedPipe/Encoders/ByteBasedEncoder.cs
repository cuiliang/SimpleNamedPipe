using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleNamedPipe;

internal class ByteBasedEncoder : IMessageEncoder
{
	public PipeTransmissionMode TransmissionMode => PipeTransmissionMode.Byte;


	private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
	private static readonly UTF8Encoding Utf8Encoding = new(false);


	public async Task WriteMessageAsync(PipeStream stream, string message)
	{
		//
		// 计算消息的UTF8字节长度
		int byteCount = Utf8Encoding.GetByteCount(message);
		int bufferLen = sizeof(int) + byteCount;
		// 从池中租用足够大的缓冲区
		byte[] buffer = _arrayPool.Rent(bufferLen);


		// 写入消息长度
		BinaryPrimitives.WriteInt32LittleEndian(buffer, byteCount);
		try
		{
			// 直接编码到缓冲区
			Utf8Encoding.GetBytes(message, 0, message.Length, buffer, sizeof(int));
			await stream.WriteAsync(buffer, 0, bufferLen);
		}
		finally
		{
			_arrayPool.Return(buffer);
		}
	}




	public async Task<string> ReadMessageAsync(PipeStream stream, CancellationToken cancellationToken)
	{
		// 读取消息长度
		byte[] lengthBuffer = new byte[sizeof(int)];

		//var read = await stream.ReadAsync(lengthBuffer, cancellationToken);
		var read = await stream.ReadAsync(lengthBuffer,0,sizeof(int), cancellationToken);


		if (read < sizeof(int))
		{
			throw new IOException("Connection closed while reading message length");
		}

		var messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
		if (messageLength <= 0) // 1MB 限制
		{
			throw new InvalidDataException($"Invalid message length: {messageLength}");
		}
		else if (messageLength > 1024 * 1024)
		{
			throw new InvalidDataException($"Message length too long: {messageLength}");
		}

		// 从共享池租用缓冲区
		var buffer = _arrayPool.Rent(messageLength);
		try
		{
			read = await stream.ReadAsync(buffer, 0, messageLength, cancellationToken);
			if (read < messageLength)
			{
				throw new IOException("Connection closed while reading message body");
			}

			// 直接解码指定长度
			return Utf8Encoding.GetString(buffer, 0, messageLength);
		}
		finally
		{
			// 归还缓冲区到池
			_arrayPool.Return(buffer);
		}

	}

}