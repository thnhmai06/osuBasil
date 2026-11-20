namespace Basil.Infrastructure.Shared.Events;

public interface IEventDelivery
{
	ValueTask DeliverAsync(CancellationToken ct);
}