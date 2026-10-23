using System.Threading.Channels;
using Basil.Application.Runtime.Events;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Runtime.Tests.Events;

/// <summary>Pins the order of delivery, the stamp of the publish moment and that one failing handler ends nothing.</summary>
public sealed class EventBusTests
{
	/// <summary>How long a test waits for a delivery before calling the bus dead.</summary>
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	/// <summary>An event whose value identifies the publish that produced it.</summary>
	/// <param name="Value">The value of the event.</param>
	private sealed record SampleEvent(string Value) : Event;

	/// <summary>Publishes every event to every handler, in the order they were published.</summary>
	[Fact]
	public async Task Publish_DeliversEveryEventToEveryHandlerInOrder()
	{
		var first = Channel.CreateUnbounded<string>();
		var second = Channel.CreateUnbounded<string>();
		using var services = Build([new CapturingHandler(first), new CapturingHandler(second)]);
		var bus = new EventBus<SampleEvent>(services, new FakeTimeProvider(), NullLogger<EventBus<SampleEvent>>.Instance);
		await bus.StartAsync(TestContext.Current.CancellationToken);

		bus.Publish(new SampleEvent("one"));
		bus.Publish(new SampleEvent("two"));
		bus.Publish(new SampleEvent("three"));

		Assert.Equal(["one", "two", "three"], await ReadAsync(first.Reader, 3));
		Assert.Equal(["one", "two", "three"], await ReadAsync(second.Reader, 3));
		await bus.StopAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>An event carries the moment it was published, not the moment a handler received it.</summary>
	[Fact]
	public async Task Publish_StampsTheMomentItWasPublished()
	{
		var received = Channel.CreateUnbounded<SampleEvent>();
		using var services = Build([new EventCapturingHandler(received)]);
		var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
		var bus = new EventBus<SampleEvent>(services, time, NullLogger<EventBus<SampleEvent>>.Instance);
		await bus.StartAsync(TestContext.Current.CancellationToken);

		time.Advance(TimeSpan.FromSeconds(42));
		bus.Publish(new SampleEvent("stamped"));

		var @event = Assert.Single(await ReadAsync(received.Reader, 1));
		Assert.Equal(time.GetUtcNow(), @event.Timestamp);
		await bus.StopAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>A handler that throws loses its own event only: the other handlers and the events after it still arrive.</summary>
	[Fact]
	public async Task Publish_KeepsDeliveringAfterAHandlerThrows()
	{
		var received = Channel.CreateUnbounded<string>();
		using var services = Build([new ThrowingHandler(), new CapturingHandler(received)]);
		var bus = new EventBus<SampleEvent>(services, new FakeTimeProvider(), NullLogger<EventBus<SampleEvent>>.Instance);
		await bus.StartAsync(TestContext.Current.CancellationToken);

		bus.Publish(new SampleEvent("before"));
		bus.Publish(new SampleEvent("after"));

		Assert.Equal(["before", "after"], await ReadAsync(received.Reader, 2));
		await bus.StopAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Stopping the server ends delivery without an exception, even with events still queued.</summary>
	[Fact]
	public async Task StopAsync_EndsWithEventsStillQueued()
	{
		using var services = Build([new CapturingHandler(Channel.CreateUnbounded<string>())]);
		var bus = new EventBus<SampleEvent>(services, new FakeTimeProvider(), NullLogger<EventBus<SampleEvent>>.Instance);
		await bus.StartAsync(TestContext.Current.CancellationToken);
		bus.Publish(new SampleEvent("queued"));

		await bus.StopAsync(TestContext.Current.CancellationToken);
	}

	private static ServiceProvider Build(IReadOnlyList<IEventHandler<SampleEvent>> handlers)
	{
		var services = new ServiceCollection();
		foreach (var handler in handlers) services.AddSingleton(handler);
		return services.BuildServiceProvider();
	}

	private static async Task<IReadOnlyList<T>> ReadAsync<T>(ChannelReader<T> reader, int count)
	{
		var values = new List<T>(count);
		for (var index = 0; index < count; index++)
			values.Add(await reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
				.WaitAsync(Patience, TestContext.Current.CancellationToken));
		return values;
	}

	/// <summary>Records the value of every event it receives.</summary>
	private sealed class CapturingHandler(Channel<string> received) : IEventHandler<SampleEvent>
	{
		/// <inheritdoc />
		public async ValueTask HandleAsync(SampleEvent @event, CancellationToken cancellationToken)
		{
			await received.Writer.WriteAsync(@event.Value, cancellationToken);
		}
	}

	/// <summary>Records every event it receives as it is.</summary>
	private sealed class EventCapturingHandler(Channel<SampleEvent> received) : IEventHandler<SampleEvent>
	{
		/// <inheritdoc />
		public async ValueTask HandleAsync(SampleEvent @event, CancellationToken cancellationToken)
		{
			await received.Writer.WriteAsync(@event, cancellationToken);
		}
	}

	/// <summary>Fails on every event it is given.</summary>
	private sealed class ThrowingHandler : IEventHandler<SampleEvent>
	{
		/// <inheritdoc />
		public ValueTask HandleAsync(SampleEvent @event, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("The handler failed.");
		}
	}
}
