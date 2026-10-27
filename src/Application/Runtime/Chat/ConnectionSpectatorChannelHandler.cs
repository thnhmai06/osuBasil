using Basil.Application.Runtime.Sessions;
using Mediator;

namespace Basil.Application.Runtime.Chat;

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