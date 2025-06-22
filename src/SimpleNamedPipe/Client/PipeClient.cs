using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleNamedPipe;
public class PipeClient : IDisposable, IAsyncDisposable
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
		MessageTransmissionMode transmissionMode = MessageTransmissionMode.ByteBasedBigEndian)
	{
		_pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		_serverName = serverName ?? throw new ArgumentNullException(nameof(serverName));


		_clientName = clientName;
		_enableClientName = !string.IsNullOrEmpty(clientName);

		// 使用信号量来控制连接和发送操作的并发
		_connectionSemaphore = new SemaphoreSlim(1, 1);
		_sendSemaphore = new SemaphoreSlim(1, 1);

		// 使用工厂方法创建编码器
		_encoder = MessageEncoderFactory.CreateEncoder(transmissionMode);
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
		if (_isAutoReconnecting)
		{
			throw new InvalidOperationException("Cannot manually connect when auto-reconnection is active. Call StopConnectWithAutoReconnection() first or use Dispose() to stop.");
		}

		await _connectionSemaphore.WaitAsync();
		try
		{
			if (IsConnected)
				throw new InvalidOperationException("Client is already connected");

			await ConnectInternalAsync(timeoutMilliseconds, false);

		}
		catch (TimeoutException tex)
		{
			// 超时未成功。
			OnError(new ErrorEventArgs("Connection timeout", tex));
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
					await ConnectInternalAsync(Timeout.Infinite, true).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					OnError(new ErrorEventArgs("Reconnection attempt failed", ex));
				}

				await Task.Delay(2000); // 等待2秒后重试连接
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
				await SendMessageInternalAsync($"CLIENTNAME:{_clientName}", _cancellationTokenSource.Token).ConfigureAwait(false);
			}

			OnConnected(new ConnectionEventArgs("Successfully connected to server"));


			// 启动消息接收任务


			if (waitReceive)
			{
				await ReceiveMessagesAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

			}
			else
			{
				_receiveTask = ReceiveMessagesAsync(_cancellationTokenSource.Token);
			}
		}
		catch (Exception ex)
		{
			OnError(new ErrorEventArgs("Connection failed", ex));
			await DisconnectInternalAsync().ConfigureAwait(false);

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
			await DisconnectInternalAsync().ConfigureAwait(false);
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
				await _receiveTask.ConfigureAwait(false);
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

	public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(message))
			throw new ArgumentNullException(nameof(message));

		await _sendSemaphore.WaitAsync(cancellationToken);
		try
		{
			await SendMessageInternalAsync(message, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_sendSemaphore.Release();
		}
	}

	private async Task SendMessageInternalAsync(string message, CancellationToken cancellationToken)
	{
		if (!IsConnected)
			throw new InvalidOperationException("Client is not connected");

		try
		{
			await _encoder.WriteMessageAsync(_pipeClient!, message, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			OnError(new ErrorEventArgs("Error sending message", ex));
			await DisconnectAsync().ConfigureAwait(false);
			throw;
		}
	}

	private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
	{
		

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				string message = await _encoder.ReadMessageAsync(_pipeClient!, cancellationToken).ConfigureAwait(false);

				if (!string.IsNullOrEmpty(message))
				{
					OnMessageReceived(new MessageReceivedEventArgs(message));
				}
			}
			catch (OperationCanceledException)
			{
				// This is an expected exception when cancellation is requested (e.g., during disconnect).
				// Break the loop silently.
				break;
			}
			catch (Exception ex)
			{
				OnError(new ErrorEventArgs("Error receiving message", ex));
				// await DisconnectAsync().ConfigureAwait(false); // Attempt to clean up the connection
				break; // Exit loop after an unhandled error
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
			// Block on DisposeAsync() for the sync Dispose() pattern.
			DisposeAsync().AsTask().GetAwaiter().GetResult();
		}

		_isDisposed = true;
	}

	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
			return;

		_isAutoReconnecting = false; // Stop any auto-reconnection attempts

		await DisconnectInternalAsync(); // Disconnects pipe, cancels CTS, awaits receive task

		_connectionSemaphore?.Dispose();
		_sendSemaphore?.Dispose();
		// _cancellationTokenSource and _pipeClient are disposed within DisconnectInternalAsync or their lifecycle management

		_isDisposed = true;
		GC.SuppressFinalize(this);
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
