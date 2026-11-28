using System.Reflection;
using System.Threading.Channels;
using Basil.Application.Shared.Events;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Shared.Events;

public sealed class EventQueue : IPublishedEvents
{
	private static readonly EventLane[] Lanes = Enum.GetValues<EventLane>();

	private readonly Channel<Item>[] _lanes;
	private readonly ILogger<EventQueue> _logger;
	private readonly AsyncLocal<Tracking?> _tracking = new();

	private long _failures;
	private int _outstanding;
	private long _published;

	public EventQueue(ILogger<EventQueue> logger)
	{
		_logger = logger;
		_lanes =
		[
			.. Lanes.Select(_ => Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true }))
		];
	}

	public int Outstanding => Volatile.Read(ref _outstanding);

	public long Published => Interlocked.Read(ref _published);

	public long Failures => Interlocked.Read(ref _failures);

	public IPublishedEventsScope Track()
	{
		var tracking = new Tracking(this);
		_tracking.Value = tracking;
		return tracking;
	}

	public void CountFailure()
	{
		Interlocked.Increment(ref _failures);
	}

	public void Enqueue(IEventDelivery delivery, EventLane lane = EventLane.General)
	{
		var done = _tracking.Value?.Add();
		Interlocked.Increment(ref _outstanding);
		Interlocked.Increment(ref _published);
		_lanes[(int)lane].Writer.TryWrite(new Item(delivery, done));
	}

	/// <summary>The lane an event type is delivered in: the one its type, or a type it derives from, declares.</summary>
	public static EventLane LaneOf<TNotification>()
	{
		return LaneCache<TNotification>.Lane;
	}

	public async Task RunAsync(CancellationToken stoppingToken)
	{
		try
		{
			await Task.WhenAll(_lanes.Select(lane =>
				Task.Run(() => RunLaneAsync(lane, stoppingToken), CancellationToken.None)));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}

	public async Task DrainAsync(CancellationToken ct = default)
	{
		while (true)
			if (TryReadAny(out var item))
				await DeliverAsync(item, ct);
			else if (Outstanding == 0)
				return;
			else
				// A delivery taken by a running lane is not finished yet; wait for it.
				await Task.Delay(TimeSpan.FromMilliseconds(1), ct);
	}

	private async Task RunLaneAsync(Channel<Item> lane, CancellationToken stoppingToken)
	{
		await foreach (var item in lane.Reader.ReadAllAsync(stoppingToken))
			await DeliverAsync(item, stoppingToken);
	}

	private bool TryReadAny(out Item item)
	{
		foreach (var lane in _lanes)
			if (lane.Reader.TryRead(out item))
				return true;

		item = default;
		return false;
	}

	private async Task DeliverAsync(Item item, CancellationToken ct)
	{
		try
		{
			await item.Delivery.DeliverAsync(ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			CountFailure();
			_logger.LogError(exception, "An event could not be delivered");
		}
		finally
		{
			Interlocked.Decrement(ref _outstanding);
			item.Done?.TrySetResult();
		}
	}

	private readonly record struct Item(IEventDelivery Delivery, TaskCompletionSource? Done);

	private static class LaneCache<TNotification>
	{
		public static readonly EventLane Lane =
			typeof(TNotification).GetCustomAttribute<EventLaneAttribute>(true)?.Lane ?? EventLane.General;
	}

	private sealed class Tracking(EventQueue queue) : IPublishedEventsScope
	{
		private readonly List<Task> _deliveries = [];
		private bool _disposed;

		// A task started inside the scope keeps it in its execution context; once disposed it tracks nothing more.
		public TaskCompletionSource? Add()
		{
			lock (_deliveries)
			{
				if (_disposed) return null;
				var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				_deliveries.Add(done.Task);
				return done;
			}
		}

		public async Task DeliveredAsync(TimeSpan limit, CancellationToken ct = default)
		{
			Task[] pending;
			lock (_deliveries)
			{
				pending = [.. _deliveries.Where(task => !task.IsCompleted)];
			}

			if (pending.Length == 0) return;

			try
			{
				await Task.WhenAll(pending).WaitAsync(limit, ct);
			}
			catch (TimeoutException)
			{
			}
		}

		public void Dispose()
		{
			lock (_deliveries)
			{
				_disposed = true;
				_deliveries.Clear();
			}

			if (ReferenceEquals(queue._tracking.Value, this)) queue._tracking.Value = null;
		}
	}
}