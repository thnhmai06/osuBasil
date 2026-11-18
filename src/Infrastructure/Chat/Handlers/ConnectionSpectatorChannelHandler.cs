using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionSpectatorChannelHandler(SpectatorChannels channels)
	: INotificationHandler<UserSessionClosed>
{
	public ValueTask Handle(UserSessionClosed @event, CancellationToken ct)
	{
		if (@event.Session is StableSession host && channels.Remove(host) is { } channel)
			channel.Close();

		return ValueTask.CompletedTask;
	}
}