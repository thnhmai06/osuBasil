using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Mediator;

namespace Basil.Application.Chat.Runtime;

/// <summary>Brings a joined spectator and, for the first spectating connection, the spectated client into the client's spectator channel.</summary>
internal sealed class SpectatorJoinedHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorJoined>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorJoined @event, CancellationToken cancellationToken)
	{
		// An HTTP API connection only watches: it takes no part in the spectator chat.
		if (@event.Spectator is ApiConnection) return ValueTask.CompletedTask;

		var channel = channels.Of(@event.Host);
		channel.ScopeTo(@event.Spectator).Join();
		if (!channel.Members.Contains(@event.Host))
			channel.ScopeTo(@event.Host).Join();

		return ValueTask.CompletedTask;
	}
}