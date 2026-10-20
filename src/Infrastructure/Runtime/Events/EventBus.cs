using System.Threading.Channels;
using Basil.Application.Objects.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Events;

/// <summary>Publishes events to their category's handlers in order.</summary>
/// <typeparam name="T">The event category delivered.</typeparam>
internal sealed class EventBus<T>(
	IServiceProvider services,
	TimeProvider time,
	ILogger<EventBus<T>> logger) : BackgroundService, IEventPublisher<T> where T : Event
{
	private readonly Channel<T> _events =
		Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

	/// <inheritdoc />
	public void Publish(T @event)
	{
		var stamped = (T)((Event)@event with { Timestamp = time.GetUtcNow() });
		_events.Writer.TryWrite(stamped);
	}

	/// <summary>Delivers events to their handlers until the server stops.</summary>
	/// <param name="stoppingToken">A token that cancels delivery when the server stops.</param>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Handlers are resolved when delivery starts, not when the bus is built: a handler reaches objects that
		// publish to buses, so resolving them in the constructor would make the buses wait for each other.
		var handlers = services.GetServices<IEventHandler<T>>().ToArray();
		await foreach (var @event in _events.Reader.ReadAllAsync(stoppingToken))
		foreach (var handler in handlers)
			try
			{
				await handler.HandleAsync(@event, stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "A handler for {Category} events failed", typeof(T).Name);
			}
	}
}