namespace Basil.Application.Shared.Events;

public interface IEventDelivery
{
	ValueTask DeliverAsync(CancellationToken ct);
}