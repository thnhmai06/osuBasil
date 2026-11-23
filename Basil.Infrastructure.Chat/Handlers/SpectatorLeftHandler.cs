using Basil.Application.Chat.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Objects.Events;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class SpectatorLeftHandler(SpectatorChannels channels) : INotificationHandler<SpectatorLeft>
{
	public ValueTask Handle(SpectatorLeft @event, CancellationToken ct)
	{
		if (@event.Spectator is ApiSession || channels.Find(@event.Host) is not { } channel)
			return ValueTask.CompletedTask;

		channel.ScopeTo(@event.Spectator).Part();
		if (@event.Host.Spectators.All(spectator => spectator is ApiSession))
			channel.ScopeTo(@event.Host).Part();

		return ValueTask.CompletedTask;
	}
}