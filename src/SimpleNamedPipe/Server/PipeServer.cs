using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SimpleNamedPipe.Encoders;

namespace SimpleNamedPipe.Server;

public class PipeServer : IDisposable
{
	// 事件定义
	public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
	public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
	public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

	private readonly string _pipeName;
	private readonly bool _enableClientName;
	private readonly ConcurrentDictionary<int, PipeClientInfo> _activeClients;
	private CancellationTokenSource? _cancellationTokenSource;
	private Task? _listenerTask;
	private int _clientCount;
	private bool _isDisposed;

	public bool IsRunning => _listenerTask != null 
	                         && _cancellationTokenSource?.Token.IsCancellationRequested != true;

	private readonly IMessageEncoder _encoder;

	public PipeServer(string pipeName, bool enableClientName = false, bool useMessageBasedEncoder = false)
	{
		_pipeName = pipeName ?? throw new ArgumentNullException(nameof(pipeName));
		_enableClientName = enableClientName;
		_activeClients = new ConcurrentDictionary<int, PipeClientInfo>();
		_clientCount = 0;

		_encoder = useMessageBasedEncoder ? new MessageBasedEncoder() : new ByteBasedEncoder();
	}

	public async Task StartAsync()
	{
		if (IsRunning)
			throw new InvalidOperationException("Server is already running");

		_cancellationTokenSource = new CancellationTokenSource();
		_listenerTask = ListenForClientsAsync(_cancellationTokenSource.Token);

		await Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		if (!IsRunning)
			return;

		_cancellationTokenSource?.Cancel();

		try
		{
			if (_listenerTask != null)
				await _listenerTask;
		}
		catch (OperationCanceledException)
		{
			// Expected exception when canceling the task
		}

		// 关闭所有客户端连接
		foreach (var client in _activeClients)
		{
			await DisconnectClientAsync(client.Key);
		}

		_activeClients.Clear();
		_listenerTask = null;
	}

	public async Task SendMessageAsync(int clientId, string message)
	{
		if (string.IsNullOrEmpty(message))
			throw new ArgumentNullException(nameof(message));

		if (!_activeClients.TryGetValue(clientId, out var clientInfo))
			throw new ArgumentException($"Client {clientId} not found");

		if (!clientInfo.PipeStream.IsConnected)
		{
			throw new IOException("连接已断开。");
		}

		await clientInfo.SendSemaphore.WaitAsync();
		try
		{
			await _encoder.WriteMessageAsync(clientInfo.PipeStream, message);
		}
		catch (Exception ex)
		{
			await DisconnectClientAsync(clientId);
			throw new IOException($"Error sending message to client {clientId}", ex);
		}
		finally
		{
			clientInfo.SendSemaphore.Release();
		}
	}

	public async Task BroadcastMessageAsync(string message)
	{
		var disconnectedClients = new ConcurrentBag<int>();

		var sendTasks = _activeClients.Select(async client =>
		{
			try
			{
				await SendMessageAsync(client.Key, message);
			}
			catch
			{
				disconnectedClients.Add(client.Key);
			}
		});

		await Task.WhenAll(sendTasks);

		foreach (var clientId in disconnectedClients)
		{
			await DisconnectClientAsync(clientId);
		}
	}

	private async Task ListenForClientsAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			NamedPipeServerStream? pipeServerStream = null;

			try
			{
				pipeServerStream = new NamedPipeServerStream(
					_pipeName,
					PipeDirection.InOut,
					NamedPipeServerStream.MaxAllowedServerInstances,
					_encoder.TransmissionMode,
					PipeOptions.Asynchronous
				);

				await pipeServerStream.WaitForConnectionAsync(cancellationToken);

				int clientId = Interlocked.Increment(ref _clientCount);
				var clientInfo = new PipeClientInfo(clientId, pipeServerStream);
				_activeClients.TryAdd(clientId, clientInfo);

				OnClientConnected(new ClientConnectedEventArgs(clientId));

				// 为每个客户端启动一个处理线程
				_ = HandleClientCommunicationAsync(clientInfo, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				pipeServerStream?.Dispose();
				break;
			}
			catch (Exception)
			{
				pipeServerStream?.Dispose();
				// 继续监听，不要因为单个客户端的错误而停止服务器
				await Task.Delay(1000, cancellationToken); // 添加延迟避免过于频繁的重试
			}
		}
	}

	private async Task HandleClientCommunicationAsync(PipeClientInfo clientInfo, CancellationToken cancellationToken)
	{
		try
		{
			
			while (clientInfo.PipeStream.IsConnected && !cancellationToken.IsCancellationRequested)
			{

				string message = await _encoder.ReadMessageAsync(clientInfo.PipeStream, cancellationToken);

				if (!string.IsNullOrEmpty(message))
				{
					// 处理客户端名称消息
					if (_enableClientName && clientInfo.ClientName == null && message.StartsWith("CLIENTNAME:"))
					{
						string clientName = message.Substring(11);
						clientInfo.ClientName = clientName;

						continue; // 不触发消息接收事件
					}

					OnMessageReceived(new MessageReceivedEventArgs(clientInfo.ClientId, message, clientInfo.ClientName));
				}
			}
		}
		catch (OperationCanceledException)
		{
			// 正常的取消操作
		}
		catch (Exception ex)
		{
			// 记录错误但继续运行
			Debug.WriteLine($"HandleClientCommunicationAsync出错：{ex.Message}");
		}
		finally
		{
			await DisconnectClientAsync(clientInfo.ClientId);
		}
	}

	private async Task DisconnectClientAsync(int clientId)
	{
		if (_activeClients.TryRemove(clientId, out var clientInfo))
		{
			try
			{
				if (clientInfo.PipeStream.IsConnected)
					clientInfo.PipeStream.Disconnect();

#if NETFRAMEWORK
				clientInfo.PipeStream.Dispose();
#else
				await clientInfo.PipeStream.DisposeAsync();
#endif

				OnClientDisconnected(new ClientDisconnectedEventArgs(clientId, clientInfo.ClientName));
			}
			catch
			{
				// 忽略关闭时的错误
			}
		}

		await Task.CompletedTask;
	}

	protected virtual void OnClientConnected(ClientConnectedEventArgs e)
	{
		ClientConnected?.Invoke(this, e);
	}

	protected virtual void OnClientDisconnected(ClientDisconnectedEventArgs e)
	{
		ClientDisconnected?.Invoke(this, e);
	}

	protected virtual void OnMessageReceived(MessageReceivedEventArgs e)
	{
		MessageReceived?.Invoke(this, e);
	}

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
			StopAsync().GetAwaiter().GetResult();
			_cancellationTokenSource?.Dispose();

			foreach (var client in _activeClients.Values)
			{
				client.PipeStream.Dispose();
			}

			_activeClients.Clear();
		}

		_isDisposed = true;
	}

	#region Types


	// 事件参数类
	public class ClientConnectedEventArgs(int clientId) : EventArgs
	{
		public int ClientId { get; } = clientId;
	}

	public class ClientDisconnectedEventArgs(int clientId, string? clientName = null) : EventArgs
	{
		public int ClientId { get; } = clientId;
		public string? ClientName { get; set; } = clientName;
	}

	public class MessageReceivedEventArgs(int clientId, string message, string? clientName = null) : EventArgs
	{
		public int ClientId { get; } = clientId;
		public string Message { get; } = message;
		public string? ClientName { get; } = clientName;
	}

	#endregion
}

