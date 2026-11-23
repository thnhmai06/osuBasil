using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects.Events;
using Basil.Domain.Sessions;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PmConnectionOpenedHandler(PmChannels channels) : INotificationHandler<SessionOpened>
{
	public ValueTask Handle(SessionOpened @event, CancellationToken ct)
	{
		if (@event.Session.Type is not SessionType.StableTourney)
			channels.Of(@event.Session.Siblings).ScopeTo(@event.Session).Join();

		return ValueTask.CompletedTask;
	}
}