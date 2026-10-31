using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Testing;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Tests.Common;

/// <summary>Pins what publishing an event does: it is stamped, queued, and delivered later to the handlers of its type.</summary>
public sealed class EventPublisherTests
{
	/// <summary>An event carries the moment it was published, not the moment a handler received it.</summary>
	[Fact]
	public async Task Publish_StampsTheMomentItWasPublished()
	{
		var handler = new RecordingHandler();
		using var world = new World(handler);

		world.Clock.Advance(TimeSpan.FromSeconds(42));
		var published = world.Clock.GetUtcNow();
		world.Publisher.Publish(new UserNotificationSent([], "stamped"));
		world.Clock.Advance(TimeSpan.FromMinutes(5));
		await world.DeliverAsync();

		Assert.Equal(published, Assert.Single(handler.Received).Timestamp);
	}

	/// <summary>Publishing returns before any handler has run.</summary>
	[Fact]
	public void Publish_RunsNoHandlerBeforeItReturns()
	{
		var handler = new RecordingHandler();
		using var world = new World(handler);

		world.Publisher.Publish(new UserNotificationSent([], "queued"));

		Assert.Empty(handler.Received);
	}

	/// <summary>An event that no handler observes is published without an error.</summary>
	[Fact]
	public async Task Publish_AcceptsAnEventNobodyHandles()
	{
		using var world = new World();

		world.Publisher.Publish(new UserNotificationSent([], "unheard"));

		await world.DeliverAsync();
	}

	/// <summary>Every handler of an event type receives it, and the events arrive in the order they were published.</summary>
	[Fact]
	public async Task Publish_DeliversEveryEventToEveryHandlerInOrder()
	{
		var first = new RecordingHandler();
		var second = new RecordingHandler();
		using var world = new World(first, second);

		world.Publisher.Publish(new UserNotificationSent([], "one"));
		world.Publisher.Publish(new UserNotificationSent([], "two"));
		world.Publisher.Publish(new UserNotificationSent([], "three"));
		await world.DeliverAsync();

		Assert.Equal(["one", "two", "three"], first.Received.Select(e => e.Text));
		Assert.Equal(["one", "two", "three"], second.Received.Select(e => e.Text));
	}

	/// <summary>A handler that throws loses its own event only: the other handlers and the later events still arrive.</summary>
	[Fact]
	public async Task Publish_KeepsDeliveringAfterAHandlerThrows()
	{
		var received = new RecordingHandler();
		using var world = new World(new ThrowingHandler(), received);

		world.Publisher.Publish(new UserNotificationSent([], "before"));
		world.Publisher.Publish(new UserNotificationSent([], "after"));
		await world.DeliverAsync();

		Assert.Equal(["before", "after"], received.Received.Select(e => e.Text));
	}

	/// <summary>Records every notification it receives as it is.</summary>
	private sealed class RecordingHandler : INotificationHandler<UserNotificationSent>
	{
		public List<UserNotificationSent> Received { get; } = [];

		/// <inheritdoc />
		public ValueTask Handle(UserNotificationSent notification, CancellationToken cancellationToken)
		{
			Received.Add(notification);
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>Fails on every notification it is given.</summary>
	private sealed class ThrowingHandler : INotificationHandler<UserNotificationSent>
	{
		/// <inheritdoc />
		public ValueTask Handle(UserNotificationSent notification, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("The handler failed.");
		}
	}

	/// <summary>The application, composed with the handlers a test gives it; its event queue runs only when a test drains it.</summary>
	private sealed class World : IDisposable
	{
		private readonly TestApplication _app;

		public World(params object[] handlers)
		{
			_app = new TestApplication(services =>
			{
				foreach (var handler in handlers)
				foreach (var contract in handler.GetType().GetInterfaces()
					         .Where(i => i.IsGenericType &&
					                     i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)))
					services.AddSingleton(contract, handler);
			});
			Publisher = _app.Provider.GetRequiredService<EventPublisher>();
		}

		public EventPublisher Publisher { get; }

		public FakeTimeProvider Clock => _app.Clock;

		public void Dispose()
		{
			_app.Dispose();
		}

		public Task DeliverAsync()
		{
			return _app.Provider.GetRequiredService<EventQueue>().DrainAsync(TestContext.Current.CancellationToken);
		}
	}
}