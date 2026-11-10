using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Records the creation of a room's match with the creator as actor.</summary>
internal sealed class LobbyRoomOpenedRecorder(IMatchEventRepository matchEvents) : INotificationHandler<LobbyRoomOpened>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomOpened @event, CancellationToken ct)
	{
		await matchEvents.RecordAsync(
			new MatchEvent(@event.Room.Match, MatchEventType.Created, @event.Timestamp, @event.Room.Authority.Creator));
	}
}