using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Common.Events;

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
	private int _outstanding;

	/// <summary>Creates an empty queue.</summary>
	/// <param name="logger">The logger that reports a delivery that failed.</param>
	/// <param name="options">The limits of the queue; the defaults when omitted.</param>
	public EventQueue(ILogger<EventQueue> logger, EventQueueOptions? options = null)
	{
		_logger = logger;
		Ceiling = Math.Max(1, (options ?? new EventQueueOptions()).Ceiling);
	}

	/// <summary>Gets how many deliveries may wait at once.</summary>
	public int Ceiling { get; }

	/// <summary>Gets how many deliveries are queued or being delivered.</summary>
	public int Outstanding => Volatile.Read(ref _outstanding);

	/// <summary>
	///     Gets whether the queue can take the consequences of new work: the last tenth of the ceiling is kept for events
	///     of work that was already accepted.
	/// </summary>
	public bool HasRoom => Outstanding < Ceiling - Ceiling / 10;

	/// <summary>Adds a delivery behind the ones already waiting; returns at once and never waits.</summary>
	/// <param name="delivery">The delivery to run.</param>
	/// <returns><see langword="false" /> when the queue is at its ceiling and the delivery was dropped.</returns>
	public bool TryEnqueue(IEventDelivery delivery)
	{
		if (Interlocked.Increment(ref _outstanding) > Ceiling || !_deliveries.Writer.TryWrite(delivery))
		{
			Interlocked.Decrement(ref _outstanding);
			EventQueueMetrics.Rejected.Add(1);
			return false;
		}

		return true;
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

	/// <summary>Delivers what is queued now and returns when no delivery is queued or still running.</summary>
	/// <param name="cancellationToken">A token that cancels the delivery.</param>
	public async Task DrainAsync(CancellationToken cancellationToken = default)
	{
		while (true)
		{
			if (_deliveries.Reader.TryRead(out var delivery))
				await DeliverAsync(delivery, cancellationToken);
			else if (Outstanding == 0)
				return;
			else
				// A delivery taken by the running loop is not finished yet; wait for it.
				await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
		}
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
		finally
		{
			Interlocked.Decrement(ref _outstanding);
		}
	}
}