using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>Records scores submitted during room rounds.</summary>
public interface IRoomRoundsService
{
	/// <summary>Records a stored score of the room's latest round.</summary>
	/// <param name="room">The room.</param>
	/// <param name="score">The relation between the stored score and the round.</param>
	/// <param name="player">The player who submitted the score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, RoundMismatch or RoomClosed when the room has closed.</returns>
	Task<RoomResult> RecordScoreAsync(Room room, RoundScore score, UserRecord player,
		CancellationToken cancellationToken = default);
}