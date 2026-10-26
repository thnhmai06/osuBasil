using System.Threading.Channels;
using Basil.Application.Runtime.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Application.Runtime.Tests.Events;

/// <summary>Pins that queued deliveries run in order, one at a time, only while the queue runs, and that a failing one ends nothing.</summary>
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
	public async Task RunAsync_RunsTheDeliveriesInOrder()
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
	public async Task Enqueue_RunsNothingUntilTheQueueRuns()
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
	public async Task RunAsync_KeepsRunningAfterADeliveryThrows()
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
	public async Task RunAsync_EndsWithoutAnExceptionWhenTheServerStopsDuringADelivery()
	{
		var queue = NewQueue();
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var running = queue.RunAsync(stop.Token);
		queue.Enqueue(new BlockedDelivery());

		await stop.CancelAsync();

		await running.WaitAsync(Patience, Token);
	}

	[Fact]
	public async Task DrainAsync_RunsWhatIsQueuedAndReturns()
	{
		var done = Channel.CreateUnbounded<string>();
		var queue = NewQueue();
		queue.Enqueue(new RecordingDelivery("one", done));
		queue.Enqueue(new RecordingDelivery("two", done));

		await queue.DrainAsync(Token);

		Assert.Equal(["one", "two"], await ReadAsync(done.Reader, 2));
	}

	private static async Task<IReadOnlyList<T>> ReadAsync<T>(ChannelReader<T> reader, int count)
	{
		var values = new List<T>(count);
		for (var index = 0; index < count; index++)
			values.Add(await reader.ReadAsync(Token).AsTask().WaitAsync(Patience, Token));
		return values;
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
