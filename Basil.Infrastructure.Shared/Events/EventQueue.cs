using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Shared.Events;

public sealed class EventQueue(ILogger<EventQueue> logger)
{
	private readonly Channel<IEventDelivery> _deliveries =
		Channel.CreateUnbounded<IEventDelivery>(new UnboundedChannelOptions { SingleReader = true });

	private long _failures;
	private int _outstanding;
	private long _published;

	public int Outstanding => Volatile.Read(ref _outstanding);

	public long Published => Interlocked.Read(ref _published);

	public long Failures => Interlocked.Read(ref _failures);

	public void CountFailure()
	{
		Interlocked.Increment(ref _failures);
	}

	public void Enqueue(IEventDelivery delivery)
	{
		Interlocked.Increment(ref _outstanding);
		Interlocked.Increment(ref _published);
		_deliveries.Writer.TryWrite(delivery);
	}

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

	public async Task DrainAsync(CancellationToken ct = default)
	{
		while (true)
			if (_deliveries.Reader.TryRead(out var delivery))
				await DeliverAsync(delivery, ct);
			else if (Outstanding == 0)
				return;
			else
				// A delivery taken by the running loop is not finished yet; wait for it.
				await Task.Delay(TimeSpan.FromMilliseconds(1), ct);
	}

	private async Task DeliverAsync(IEventDelivery delivery, CancellationToken ct)
	{
		try
		{
			await delivery.DeliverAsync(ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			CountFailure();
			logger.LogError(exception, "An event could not be delivered");
		}
		finally
		{
			Interlocked.Decrement(ref _outstanding);
		}
	}
}