using Basil.Application.Chat.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class SpectatorLeftHandler(SpectatorChannels channels) : INotificationHandler<UserSpectatorLeft>
{
	public ValueTask Handle(UserSpectatorLeft @event, CancellationToken ct)
	{
		if (@event.Spectator is ApiConnection || channels.Find(@event.Host) is not { } channel)
			return ValueTask.CompletedTask;

		channel.ScopeTo(@event.Spectator).Part();
		if (@event.Host.Spectators.All(spectator => spectator is ApiConnection))
			channel.ScopeTo(@event.Host).Part();

		return ValueTask.CompletedTask;
	}
}