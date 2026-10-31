using Basil.Host.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Basil.Host.Tests.Logging;

/// <summary>The queue behind the log writer is bounded: when it is full events are dropped and counted, never waited for.</summary>
public sealed class LogQueueTests
{
	[Fact]
	public void AFullQueueDropsTheNewestEventsAndCountsThem()
	{
		const int capacity = 8, total = 100;
		var drops = new LogDrops();
		var sink = new BlockedSink();
		var logger = ServerLogging.Create(configuration => configuration.Sink(sink), capacity, drops);

		for (var index = 0; index < total; index++) logger.Information("event {Index}", index);
		var droppedWhileBlocked = drops.Dropped;
		sink.Release();
		logger.Dispose();

		// One event can be inside the writer and `capacity` more wait in the queue; the rest cannot be kept.
		Assert.True(droppedWhileBlocked >= total - capacity - 1, $"dropped {droppedWhileBlocked}");
		Assert.True(droppedWhileBlocked < total);
		Assert.Equal(total, sink.Written + drops.Dropped);
	}

	[Fact]
	public void AQueueThatIsNotFullDropsNothing()
	{
		var drops = new LogDrops();
		var sink = new BlockedSink();
		var logger = ServerLogging.Create(configuration => configuration.Sink(sink), 100, drops);

		for (var index = 0; index < 10; index++) logger.Information("event {Index}", index);
		sink.Release();
		logger.Dispose();

		Assert.Equal(0, drops.Dropped);
		Assert.Equal(10, sink.Written);
	}

	private sealed class BlockedSink : ILogEventSink
	{
		private readonly ManualResetEventSlim _release = new();
		private int _written;

		public int Written => Volatile.Read(ref _written);

		public void Emit(LogEvent logEvent)
		{
			_release.Wait();
			Interlocked.Increment(ref _written);
		}

		public void Release()
		{
			_release.Set();
		}
	}
}