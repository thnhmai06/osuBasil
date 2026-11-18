using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class LobbyRoomOpenedRecorder(IMatchEventRepository matchEvents) : INotificationHandler<LobbyRoomOpened>
{
	public async ValueTask Handle(LobbyRoomOpened @event, CancellationToken ct)
	{
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.Created, @event.Timestamp, @event.Room.Authority.Creator));
	}
}