using Basil.Application.Runtime.Sessions;
using Mediator;

namespace Basil.Application.Runtime.Chat;

/// <summary>Brings a joined spectator and, for the first spectating connection, the spectated client into the client's spectator channel.</summary>
internal sealed class SpectatorJoinedHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorJoined>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorJoined @event, CancellationToken cancellationToken)
	{
		var channel = channels.Of(@event.Host);
		channel.ScopeTo(@event.Spectator).Join();
		if (!channel.Members.Contains(@event.Host))
			channel.ScopeTo(@event.Host).Join();

		return ValueTask.CompletedTask;
	}
}