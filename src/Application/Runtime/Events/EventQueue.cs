using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Runtime.Events;

/// <summary>
///     The one queue every published event waits in, so that handlers run one at a time, in the order the events were
///     published, and never on the thread that published them.
/// </summary>
/// <remarks>
///     Nothing runs until <see cref="RunAsync" /> does: the host runs it for as long as the server lives. A handler
///     that fails is reported and does not stop the ones after it.
/// </remarks>
public sealed class EventQueue
{
	private readonly Channel<IEventDelivery> _deliveries =
		Channel.CreateUnbounded<IEventDelivery>(new UnboundedChannelOptions { SingleReader = true });

	private readonly ILogger<EventQueue> _logger;

	/// <summary>Creates an empty queue.</summary>
	/// <param name="logger">The logger that reports a delivery that failed.</param>
	public EventQueue(ILogger<EventQueue> logger)
	{
		_logger = logger;
	}

	/// <summary>Adds a delivery behind the ones already waiting; returns at once.</summary>
	/// <param name="delivery">The delivery to run.</param>
	internal void Enqueue(IEventDelivery delivery)
	{
		_deliveries.Writer.TryWrite(delivery);
	}

	/// <summary>Delivers the queued events, and the ones queued later, until the server stops.</summary>
	/// <param name="stoppingToken">A token that ends the delivery when the server stops.</param>
	public async Task RunAsync(CancellationToken stoppingToken)
	{
		try
		{
			await foreach (var delivery in _deliveries.Reader.ReadAllAsync(stoppingToken))
				await DeliverAsync(delivery, stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
	}

	/// <summary>Delivers what is queued now and returns when the queue is empty.</summary>
	/// <param name="cancellationToken">A token that cancels the delivery.</param>
	internal async Task DrainAsync(CancellationToken cancellationToken = default)
	{
		while (_deliveries.Reader.TryRead(out var delivery))
			await DeliverAsync(delivery, cancellationToken);
	}

	private async Task DeliverAsync(IEventDelivery delivery, CancellationToken cancellationToken)
	{
		try
		{
			await delivery.DeliverAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "An event could not be delivered");
		}
	}
}
