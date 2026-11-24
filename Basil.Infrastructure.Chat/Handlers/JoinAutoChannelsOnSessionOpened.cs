using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class JoinAutoChannelsOnSessionOpened(GeneralChannelRegistry channels)
	: INotificationHandler<SessionOpened>
{
	public ValueTask Handle(SessionOpened @event, CancellationToken ct)
	{
		channels.JoinAutoChannels(@event.Session);
		return ValueTask.CompletedTask;
	}
}
