using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class ConnectionSpectatorChannelHandler(SpectatorChannels channels)
	: INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		if (@event.Connection is StableConnection host && channels.Remove(host) is { } channel)
			channel.Close();

		return ValueTask.CompletedTask;
	}
}