using Basil.Application.Services.Contracts.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Events;

/// <summary>Delivers one category of events to the handlers that react to it.</summary>
/// <remarks>
///     Reads the event stream even when no handler is registered, so events do not pile up. One handler failing does not
///     stop the others.
/// </remarks>
/// <typeparam name="T">The event category delivered.</typeparam>
internal sealed class EventPump<T> : BackgroundService where T : Event
{
	private readonly IEventPublisher<T> source;
	private readonly IEnumerable<IEventHandler<T>> handlers;
	private readonly ILogger<EventPump<T>> logger;

	/// <summary>Creates a pump over one event stream.</summary>
	/// <param name="source">The service whose events are delivered.</param>
	/// <param name="handlers">The handlers to call for each event, in registration order.</param>
	/// <param name="logger">The logger for a handler that fails.</param>
	public EventPump(IEventPublisher<T> source, IEnumerable<IEventHandler<T>> handlers, ILogger<EventPump<T>> logger)
	{
		this.source = source;
		this.handlers = handlers;
		this.logger = logger;
	}

	/// <summary>Reads the event stream until the server stops.</summary>
	/// <param name="stoppingToken">A token that cancels the pump when the server stops.</param>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var @event in source.Events.ReadAllAsync(stoppingToken).ConfigureAwait(false))
		{
			foreach (var handler in handlers)
			{
				try
				{
					await handler.HandleAsync(@event, stoppingToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "A handler for {Category} events failed.", typeof(T).Name);
				}
			}
		}
	}
}
