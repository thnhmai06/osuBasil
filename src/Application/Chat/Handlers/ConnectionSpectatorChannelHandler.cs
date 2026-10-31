using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Application.Chat.Handlers;

/// <summary>Closes the spectator channel of a closed osu! client and forgets it.</summary>
internal sealed class ConnectionSpectatorChannelHandler(SpectatorChannels channels)
	: INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken cancellationToken)
	{
		if (@event.Connection is BanchoConnection host && channels.Remove(host) is { } channel)
			channel.Close();

		return ValueTask.CompletedTask;
	}
}