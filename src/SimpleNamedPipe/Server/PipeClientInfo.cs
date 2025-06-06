using System;
using System.IO.Pipes;
using System.Threading;

namespace SimpleNamedPipe;


// 客户端信息类
public class PipeClientInfo : IDisposable
{
	public int ClientId { get; }
	public string? ClientName { get; set; }
	public NamedPipeServerStream PipeStream { get; }

	/// <summary>
	/// 用于存储客户端信息
	/// </summary>
	public object? Tag { get; set; }



	public  SemaphoreSlim SendSemaphore
	{
		get;
	} = new SemaphoreSlim(1, 1);

	private bool _disposed = false;

	public PipeClientInfo(int clientId, NamedPipeServerStream pipeStream)
	{
		ClientId = clientId;
		PipeStream = pipeStream;
		ClientName = null;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed) return;

		if (disposing)
		{
			// Dispose managed resources
			PipeStream.Dispose();
			SendSemaphore.Dispose();
		}

		_disposed = true;
	}

	// Optional: Finalizer, if you have unmanaged resources or want a safety net.
	// However, for purely managed resources like PipeStream and SemaphoreSlim,
	// the Dispose pattern is generally sufficient if Dispose() is reliably called.
	// ~PipeClientInfo()
	// {
	//     Dispose(false);
	// }
}