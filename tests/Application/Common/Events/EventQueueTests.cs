using System.Diagnostics.Metrics;
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

	private static EventQueue NewQueue(int? ceiling = null)
	{
		return new EventQueue(NullLogger<EventQueue>.Instance,
			ceiling is { } value ? new EventQueueOptions { Ceiling = value } : null);
	}

	private static long CountRejections(Action action)
	{
		long rejected = 0;
		using var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, meterListener) =>
		{
			if (instrument.Name == EventQueueMetrics.RejectedName) meterListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref rejected, value));
		listener.Start();
		action();
		return Interlocked.Read(ref rejected);
	}

	[Fact]
	public void TryEnqueue_BelowTheCeiling_AcceptsAndCountsNothingRejected()
	{
		var queue = NewQueue(2);
		var results = new List<bool>();

		var rejected = CountRejections(() => results.AddRange([
			queue.TryEnqueue(new ThrowingDelivery()), queue.TryEnqueue(new ThrowingDelivery())
		]));

		Assert.Equal([true, true], results);
		Assert.Equal(2, queue.Outstanding);
		Assert.Equal(0, rejected);
	}

	[Fact]
	public void TryEnqueue_AtTheCeiling_RejectsWithoutQueueingAndCountsTheRejection()
	{
		var queue = NewQueue(2);
		queue.TryEnqueue(new ThrowingDelivery());
		queue.TryEnqueue(new ThrowingDelivery());
		var accepted = true;

		var rejected = CountRejections(() => accepted = queue.TryEnqueue(new ThrowingDelivery()));

		Assert.False(accepted);
		Assert.Equal(2, queue.Outstanding);
		Assert.Equal(1, rejected);
	}

	[Fact]
	public async Task TryEnqueue_AcceptsAgainOnceDeliveriesHaveRun()
	{
		var queue = NewQueue(1);
		queue.TryEnqueue(new ThrowingDelivery());
		Assert.False(queue.TryEnqueue(new ThrowingDelivery()));

		await queue.DrainAsync(Token);

		Assert.True(queue.TryEnqueue(new ThrowingDelivery()));
	}

	[Theory]
	[InlineData(10, 8, true)]
	[InlineData(10, 9, false)]
	[InlineData(100, 89, true)]
	[InlineData(100, 90, false)]
	public void HasRoom_KeepsTheLastTenthForWorkAlreadyAccepted(int ceiling, int outstanding, bool expected)
	{
		var queue = NewQueue(ceiling);
		for (var i = 0; i < outstanding; i++) queue.TryEnqueue(new ThrowingDelivery());

		Assert.Equal(expected, queue.HasRoom);
	}

	[Fact]
	public void TheDefaultCeiling_IsOneHundredThousand()
	{
		Assert.Equal(100_000, NewQueue().Ceiling);
	}

	[Fact]
	public async Task RunAsync_RunsTheDeliveriesInOrder()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);

		queue.TryEnqueue(new RecordingDelivery("one", done));
		queue.TryEnqueue(new RecordingDelivery("two", done));
		queue.TryEnqueue(new RecordingDelivery("three", done));

		Assert.Equal(["one", "two", "three"], await ReadAsync(done.Reader, 3));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task Enqueue_RunsNothingUntilTheQueueRuns()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();

		queue.TryEnqueue(new RecordingDelivery("queued", done));

		Assert.False(done.Reader.TryRead(out _));
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);
		Assert.Equal(["queued"], await ReadAsync(done.Reader, 1));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task RunAsync_KeepsRunningAfterADeliveryThrows()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);

		queue.TryEnqueue(new ThrowingDelivery());
		queue.TryEnqueue(new RecordingDelivery("after", done));

		Assert.Equal(["after"], await ReadAsync(done.Reader, 1));
		await stop.CancelAsync();
		await running;
	}

	[Fact]
	public async Task RunAsync_EndsWithoutAnExceptionWhenTheServerStopsDuringADelivery()
	{
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);
		queue.TryEnqueue(new BlockedDelivery());

		await stop.CancelAsync();

		await running.WaitAsync(Patience, Token);
	}

	[Fact]
	public async Task DrainAsync_RunsWhatIsQueuedAndReturns()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		queue.TryEnqueue(new RecordingDelivery("one", done));
		queue.TryEnqueue(new RecordingDelivery("two", done));

		await queue.DrainAsync(Token);

		Assert.Equal(["one", "two"], await ReadAsync(done.Reader, 2));
	}

	[Fact]
	public async Task DrainAsync_WaitsForADeliveryTheRunningLoopAlreadyTook()
	{
		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var finished = false;
		var queue = NewQueue();
		using var stop = new CancellationTokenSource();
		var running = queue.RunAsync(stop.Token);
		queue.TryEnqueue(new GatedDelivery(started, release, () => finished = true));
		await started.Task.WaitAsync(Patience, Token);

		var drain = queue.DrainAsync(Token);
		release.SetResult();
		await drain.WaitAsync(Patience, Token);

		Assert.True(finished);
		await stop.CancelAsync();
		await running.WaitAsync(Patience, Token);
	}

	/// <summary>Announces that it started, then waits until it is released.</summary>
	private sealed class GatedDelivery(TaskCompletionSource started, TaskCompletionSource release, Action finish)
		: IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken cancellationToken)
		{
			started.SetResult();
			await release.Task;
			finish();
		}
	}

	private static async Task<IReadOnlyList<T>> ReadAsync<T>(ChannelReader<T> reader, int count)
	{
		var values = new List<T>(count);
		for (var index = 0; index < count; index++)
			values.Add(await reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token));
		return values;
	}

	[Fact]
	public async Task Outstanding_CountsQueuedDeliveriesUntilTheyHaveRun_EvenWhenTheyFail()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();

		queue.TryEnqueue(new RecordingDelivery("one", done));
		queue.TryEnqueue(new ThrowingDelivery());
		Assert.Equal(2, queue.Outstanding);

		await queue.DrainAsync(Token);

		Assert.Equal(0, queue.Outstanding);
	}

	/// <summary>Reports its name when it runs.</summary>
	private sealed class RecordingDelivery(string name, Channel<string> done) : IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken cancellationToken)
		{
			await done.Writer.WriteAsync(name, cancellationToken);
		}
	}

	/// <summary>Fails when it runs.</summary>
	private sealed class ThrowingDelivery : IEventDelivery
	{
		/// <inheritdoc />
		public ValueTask DeliverAsync(CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("The delivery failed.");
		}
	}

	/// <summary>Runs until the server stops.</summary>
	private sealed class BlockedDelivery : IEventDelivery
	{
		/// <inheritdoc />
		public async ValueTask DeliverAsync(CancellationToken cancellationToken)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}
	}
}