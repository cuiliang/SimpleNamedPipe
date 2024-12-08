using SimpleNamedPipe.Encoders;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleNamedPipe.Client;
public class PipeClient : IDisposable
{
	// 事件定义
	public event EventHandler<ConnectionEventArgs>? Connected;
	public event EventHandler<ConnectionEventArgs>? Disconnected;
	public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
	public event EventHandler<ErrorEventArgs>? Error;
	public event EventHandler<ReconnectingEventArgs>? Reconnecting;

	private readonly string _serverName;
	private readonly string _pipeName;
	private readonly bool _enableClientName;
	private readonly string? _clientName;
	private NamedPipeClientStream? _pipeClient;
	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _receiveTask;
	private volatile bool _isDisposed;
	private volatile bool _isAutoReconnecting;  // 是否自动重连

	private readonly SemaphoreSlim _connectionSemaphore;
	private readonly SemaphoreSlim _sendSemaphore;
	private readonly IMessageEncoder _encoder;

	public bool IsConnected => _pipeClient?.IsConnected ?? false;
	public string? ClientName => _clientName;

	public PipeClient(
		string pipeName,
		string serverName = ".",
		string? clientName = null,
		bool useMessageBasedEncoder = false)
	{
		_pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		_serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));


		_clientName = clientName;
		_enableClientName = !string.IsNullOrEmpty(clientName);

		// 使用信号量来控制连接和发送操作的并发
		_connectionSemaphore = new SemaphoreSlim(1, 1);
		_sendSemaphore = new SemaphoreSlim(1, 1);

		_encoder = useMessageBasedEncoder ? new MessageBasedEncoder() : new ByteBasedEncoder();
	}

	#region 连接与断开

	/// <summary>
	/// 连接到服务器。
	/// 一次性连接。
	/// </summary>
	/// <param name="timeoutMilliseconds">超时时间</param>
	/// <returns></returns>
	/// <exception cref="InvalidOperationException"></exception>
	public async Task ConnectAsync(int timeoutMilliseconds = 5000)
	{
		await _connectionSemaphore.WaitAsync();
		try
		{
			if (IsConnected)
				throw new InvalidOperationException("Client is already connected");

			await ConnectInternalAsync(timeoutMilliseconds, false);

		}
		catch (TimeoutException)
		{
			// 超时未成功。
			Console.WriteLine("Connection Timeout.");
		}
		finally
		{
			_connectionSemaphore.Release();
		}
	}

	/// <summary>
	/// 开始连接到服务器，并在连接断开时自动重连。
	/// </summary>
	public void StartConnectWithAutoReconnection()
	{
		if (_isAutoReconnecting || _isDisposed)
			return;

		_isAutoReconnecting = true;
		Task.Run(async () =>
		{
			while (_isAutoReconnecting && !_isDisposed)
			{
				try
				{
					await ConnectInternalAsync(Timeout.Infinite, true);
				}
				catch (Exception ex)
				{
					OnError(new ErrorEventArgs("Reconnection attempt failed", ex));
				}
			}
		});
	}

	/// <summary>
	/// 连接到服务器，并根据情况等待接收消息（完成一次连接过程）
	/// </summary>
	/// <param name="timeoutMilliseconds"></param>
	/// <param name="waitReceive"></param>
	/// <returns></returns>
	private async Task ConnectInternalAsync(int timeoutMilliseconds, bool waitReceive)
	{
		_cancellationTokenSource?.Dispose();
		_cancellationTokenSource = new CancellationTokenSource();

		_pipeClient?.Dispose();
		_pipeClient = new NamedPipeClientStream(
			_serverName,
			_pipeName,
			PipeDirection.InOut,
			PipeOptions.Asynchronous
		);



		try
		{

			await _pipeClient.ConnectAsync(timeoutMilliseconds, _cancellationTokenSource.Token).ConfigureAwait(false);
			_pipeClient.ReadMode = _encoder.TransmissionMode;



			// 如果启用了客户端名称功能，发送客户端名称
			if (_enableClientName && !string.IsNullOrEmpty(_clientName))
			{
				await SendMessageInternalAsync($"CLIENTNAME:{_clientName}");
			}

			OnConnected(new ConnectionEventArgs("Successfully connected to server"));


			// 启动消息接收任务


			if (waitReceive)
			{
				await ReceiveMessagesAsync(_cancellationTokenSource.Token);

			}
			else
			{
				_receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
			}
		}
		catch (Exception ex)
		{
			OnError(new ErrorEventArgs("Connection failed", ex));
			await DisconnectInternalAsync();

			throw;
			//if (_autoReconnect && !_isDisposed)
			//{
			//	StartReconnection();
			//}
			//else
			//{
			//	throw;
			//}
		}
	}

	public async Task DisconnectAsync()
	{
		await _connectionSemaphore.WaitAsync();
		try
		{
			await DisconnectInternalAsync();
		}
		finally
		{
			_connectionSemaphore.Release();
		}
	}

	private async Task DisconnectInternalAsync()
	{


		if (_cancellationTokenSource != null)
		{
			_cancellationTokenSource.Cancel();
			_cancellationTokenSource.Dispose();
			_cancellationTokenSource = null;
		}

		if (_pipeClient != null)
		{
			try
			{
				if (_pipeClient.IsConnected)
				{
					_pipeClient.Close();
				}
			}
			catch (Exception ex)
			{
				OnError(new ErrorEventArgs("Error closing pipe", ex));
			}
			finally
			{
				_pipeClient.Dispose();
				_pipeClient = null;
			}
		}

		try
		{
			if (_receiveTask != null)
			{
				await _receiveTask;
				_receiveTask = null;
			}
		}
		catch (OperationCanceledException)
		{
			// Expected exception
		}
		catch (Exception ex)
		{
			OnError(new ErrorEventArgs("Error during disconnect", ex));
		}

		OnDisconnected(new ConnectionEventArgs("Disconnected from server"));

		//if (_autoReconnect && !_isDisposed)
		//{
		//	StartReconnection();
		//}
	}

	#endregion

	#region 消息发送与接收

	public async Task SendMessageAsync(string message)
	{
		if (string.IsNullOrEmpty(message))
			throw new ArgumentNullException(nameof(message));

		await _sendSemaphore.WaitAsync();
		try
		{
			await SendMessageInternalAsync(message);
		}
		finally
		{
			_sendSemaphore.Release();
		}
	}

	private async Task SendMessageInternalAsync(string message)
	{
		if (!IsConnected)
			throw new InvalidOperationException("Client is not connected");

		try
		{
			await _encoder.WriteMessageAsync(_pipeClient!, message);
		}
		catch (Exception ex)
		{
			OnError(new ErrorEventArgs("Error sending message", ex));
			await DisconnectAsync();
			throw;
		}
	}

	private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
	{
		

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				string message = await _encoder.ReadMessageAsync(_pipeClient!, cancellationToken);

				if (!string.IsNullOrEmpty(message))
				{
					OnMessageReceived(new MessageReceivedEventArgs(message));
				}
			}
			//catch (OperationCanceledException)
			//{
			//	break;
			//}
			catch (Exception ex)
			{
				OnError(new ErrorEventArgs("Error receiving message", ex));
				await DisconnectAsync();
				break;
			}
		}
	}

	#endregion

	#region 事件

	protected virtual void OnConnected(ConnectionEventArgs e)
	{
		Connected?.Invoke(this, e);
	}

	protected virtual void OnDisconnected(ConnectionEventArgs e)
	{
		Disconnected?.Invoke(this, e);
	}

	protected virtual void OnMessageReceived(MessageReceivedEventArgs e)
	{
		MessageReceived?.Invoke(this, e);
	}

	protected virtual void OnError(ErrorEventArgs e)
	{
		Error?.Invoke(this, e);
	}

	protected virtual void OnReconnecting(ReconnectingEventArgs e)
	{
		Reconnecting?.Invoke(this, e);
	}

	#endregion

	#region 释放

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_isDisposed)
			return;

		if (disposing)
		{
			_isDisposed = true;
			_isAutoReconnecting = false;

			DisconnectAsync().GetAwaiter().GetResult();

			_connectionSemaphore.Dispose();
			_sendSemaphore.Dispose();
		}

		_isDisposed = true;
	}

	#endregion


	#region Types


	// 事件参数类
	public class ConnectionEventArgs : EventArgs
	{
		public string Message { get; }

		public ConnectionEventArgs(string message)
		{
			Message = message;
		}
	}

	public class ErrorEventArgs : EventArgs
	{
		public string Message { get; }
		public Exception Exception { get; }

		public ErrorEventArgs(string message, Exception exception)
		{
			Message = message;
			Exception = exception;
		}
	}

	public class ReconnectingEventArgs : EventArgs
	{
		public string Message { get; }

		public ReconnectingEventArgs(string message)
		{
			Message = message;
		}
	}


	public class MessageReceivedEventArgs : EventArgs
	{
		public string Message { get; }

		public MessageReceivedEventArgs(string message)
		{
			Message = message;
		}
	}

	#endregion
}
