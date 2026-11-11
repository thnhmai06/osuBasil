using System.Threading.Channels;
using Basil.Application.Common.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Common.Tests.Events;

/// <summary>
///     Pins that queued deliveries run in order, one at a time, only while the queue runs, and that a failing one
///     ends nothing.
/// </summary>
public sealed class EventQueueTests
{
	/// <summary>How long a test waits for a delivery before calling the queue dead.</summary>
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static EventQueue NewQueue()
	{
		return new EventQueue(NullLogger<EventQueue>.Instance);
	}

	[Fact]
	public async Task Run_DeliveriesInOrder()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);

		queue.Enqueue(new RecordingDelivery("one", done));
		queue.Enqueue(new RecordingDelivery("two", done));
		queue.Enqueue(new RecordingDelivery("three", done));

		Assert.Equal(["one", "two", "three"], await ReadAsync(done.Reader, 3));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task Enqueue_NothingRunsYet()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();

		queue.Enqueue(new RecordingDelivery("queued", done));

		Assert.False(done.Reader.TryRead(out _));
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);
		Assert.Equal(["queued"], await ReadAsync(done.Reader, 1));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task AfterThrow_KeepsRunning()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);

		queue.Enqueue(new ThrowingDelivery());
		queue.Enqueue(new RecordingDelivery("after", done));

		Assert.Equal(["after"], await ReadAsync(done.Reader, 1));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task StopDuringDelivery_NoException()
	{
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);
		queue.Enqueue(new BlockedDelivery());

		await stop.CancelAsync();

		await running.WaitAsync(Patience, Token);
	}

	[Fact]
	public async Task Drain_QueuedRuns()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		queue.Enqueue(new RecordingDelivery("one", done));
		queue.Enqueue(new RecordingDelivery("two", done));

		await queue.DrainAsync(Token);

		Assert.Equal(["one", "two"], await ReadAsync(done.Reader, 2));
	}

	[Fact]
	public async Task DrainInFlight_Waited()
	{
		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var finished = false;
		var queue = NewQueue();
		using var stop = new CancellationTokenSource();
		var running = queue.RunAsync(stop.Token);
		queue.Enqueue(new GatedDelivery(started, release, () => finished = true));
		await started.Task.WaitAsync(Patience, Token);

		var drain = queue.DrainAsync(Token);
		release.SetResult();
		await drain.WaitAsync(Patience, Token);

		Assert.True(finished);
		await stop.CancelAsync();
		await running.WaitAsync(Patience, Token);
	}

	private static async Task<IReadOnlyList<T>> ReadAsync<T>(ChannelReader<T> reader, int count)
	{
		var values = new List<T>(count);
		for (var index = 0; index < count; index++)
			values.Add(await reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token));
		return values;
	}

	[Fact]
	public async Task Outstanding_CountedUntilRun()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();

		queue.Enqueue(new RecordingDelivery("one", done));
		queue.Enqueue(new ThrowingDelivery());
		Assert.Equal(2, queue.Outstanding);

		await queue.DrainAsync(Token);

		Assert.Equal(0, queue.Outstanding);
	}

	/// <summary>Announces that it started, then waits until it is released.</summary>
	private sealed class GatedDelivery(TaskCompletionSource started, TaskCompletionSource release, Action finish)
		: IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken ct)
		{
			started.SetResult();
			await release.Task;
			finish();
		}
	}

	/// <summary>Reports its name when it runs.</summary>
	private sealed class RecordingDelivery(string name, Channel<string> done) : IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken ct)
		{
			await done.Writer.WriteAsync(name, ct);
		}
	}

	/// <summary>Fails when it runs.</summary>
	private sealed class ThrowingDelivery : IEventDelivery
	{
		/// <inheritdoc />
		public ValueTask DeliverAsync(CancellationToken ct)
		{
			throw new InvalidOperationException("The delivery failed.");
		}
	}

	/// <summary>Runs until the server stops.</summary>
	private sealed class BlockedDelivery : IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken ct)
		{
			await Task.Delay(Timeout.Infinite, ct);
		}
	}
}