using Mediator;

namespace Basil.Application.Shared.Events;

public abstract record Event : INotification
{
	public DateTimeOffset Timestamp { get; internal set; }
}