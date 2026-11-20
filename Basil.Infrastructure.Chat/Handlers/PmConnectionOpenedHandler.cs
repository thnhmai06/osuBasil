using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class PmConnectionOpenedHandler(PmChannels channels) : INotificationHandler<UserSessionOpened>
{
	public ValueTask Handle(UserSessionOpened @event, CancellationToken ct)
	{
		if (@event.Session.Type is not SessionType.StableTourney)
			channels.Of(@event.Session.User).ScopeTo(@event.Session).Join();

		return ValueTask.CompletedTask;
	}
}