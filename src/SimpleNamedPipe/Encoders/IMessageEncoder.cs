using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleNamedPipe;

internal interface IMessageEncoder
{
	/// <summary>
	/// 对应的管道传输模式
	/// </summary>
	PipeTransmissionMode TransmissionMode { get; }

	Task WriteMessageAsync(PipeStream stream, string message, CancellationToken cancellationToken);
	Task<string> ReadMessageAsync(PipeStream stream, CancellationToken cancellationToken);
}