using System;
using System.Collections.Concurrent;
using LiveDanmuDesktop.Models;

namespace LiveDanmuDesktop.Services;

public class MessageAggregator : IDisposable
{
	private readonly ConcurrentQueue<LiveMessage> _messageQueue = new ConcurrentQueue<LiveMessage>();

	private const int MaxQueueSize = 10000;

	private bool _disposed;

	public event EventHandler<LiveMessage>? MessageReceived;

	public void PublishMessage(LiveMessage message)
	{
		if (!_disposed)
		{
			if (_messageQueue.Count >= 10000)
			{
				_messageQueue.TryDequeue(out _);
			}
			_messageQueue.Enqueue(message);
			this.MessageReceived?.Invoke(this, message);
		}
	}

	public int GetQueueCount()
	{
		return _messageQueue.Count;
	}

	public bool TryDequeueMessage(out LiveMessage? message)
	{
		return _messageQueue.TryDequeue(out message);
	}

	public void ClearQueue()
	{
		while (_messageQueue.TryDequeue(out _))
		{
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			ClearQueue();
		}
	}
}
