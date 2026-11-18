using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class SpectatorJoinedHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorJoined>
{
	public ValueTask Handle(UserSpectatorJoined @event, CancellationToken ct)
	{
		// An HTTP API connection only watches: it takes no part in the spectator chat.
		if (@event.Spectator is ApiSession) return ValueTask.CompletedTask;

		var channel = channels.Of(@event.Host);
		channel.ScopeTo(@event.Spectator).Join();
		if (!channel.Members.Contains(@event.Host))
			channel.ScopeTo(@event.Host).Join();

		return ValueTask.CompletedTask;
	}
}