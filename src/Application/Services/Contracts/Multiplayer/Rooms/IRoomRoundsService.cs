using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Domain.Multiplayer.Rounds;
using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Rounds and the scores recorded in them.</summary>
public interface IRoomRoundsService
{
	/// <summary>Starts the next round; players who have the beatmap start playing.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, NoBeatmap or RoomClosed when the room has closed.</returns>
	Task<RoomResult> StartAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Aborts the round in progress; players go back to not ready.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">A manager.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> AbortAsync(Room room, Connection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller finished loading the beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; reporting again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to load emits <see cref="RoomRoundAllLoaded" /> instead of <see cref="RoomRoundPlayerLoaded" />.
	/// </remarks>
	Task<RoomResult> MarkLoadedAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller wants to skip the intro.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed; asking again returns Ok and does nothing.</returns>
	/// <remarks>
	///     The last player to ask emits <see cref="RoomRoundAllSkipped" /> instead of <see cref="RoomRoundPlayerSkipped" />.
	/// </remarks>
	Task<RoomResult> SkipAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller failed; the player keeps playing until completion.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	Task<RoomResult> FailAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Reports that the caller completed the beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotPlaying or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     When the last player completes, the round ends and <see cref="RoomRoundCompleted" /> is emitted instead of
	///     <see cref="RoomRoundPlayerCompleted" />.
	/// </remarks>
	Task<RoomResult> CompleteAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Records a stored score of the room's latest round.</summary>
	/// <param name="room">The room.</param>
	/// <param name="score">The relation between the stored score and the round.</param>
	/// <param name="player">The player who submitted the score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, RoundMismatch or RoomClosed when the room has closed.</returns>
	Task<RoomResult> RecordScoreAsync(Room room, RoundScore score, User player,
		CancellationToken cancellationToken = default);
}