using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class SpectatorJoinedHandler(SpectatorChannels channels) : INotificationHandler<SpectatorJoined>
{
	public ValueTask Handle(SpectatorJoined @event, CancellationToken ct)
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