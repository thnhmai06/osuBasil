using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Records scores submitted during room rounds.</summary>
internal sealed class RoomRoundsService(IEventPublisher<RoomEvent> events) : IRoomRoundsService
{
	/// <inheritdoc />
	public Task<RoomResult> RecordScoreAsync(Room room, RoundScore score, UserRecord player,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => RecordScore(room, score, player), cancellationToken);
	}

	private RoomResult RecordScore(Room room, RoundScore score, UserRecord player)
	{
		if (room.Rounds.LastRound is not { } round || !round.Equals(score.Round)) return RoomResult.RoundMismatch;

		events.Publish(new RoomRoundScoreSubmitted(room, round, player, score));
		return RoomResult.Ok;
	}
}