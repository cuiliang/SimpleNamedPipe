using System.IO.Pipes;
using System.Threading;

namespace SimpleNamedPipe.Server;


// 客户端信息类
public class PipeClientInfo
{
	public int ClientId { get; }
	public string? ClientName { get; set; }
	public NamedPipeServerStream PipeStream { get; }

	public  SemaphoreSlim SendSemaphore
	{
		get;
	} = new SemaphoreSlim(1, 1);

	public PipeClientInfo(int clientId, NamedPipeServerStream pipeStream)
	{
		ClientId = clientId;
		PipeStream = pipeStream;
		ClientName = null;
	}
}