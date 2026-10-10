using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>The room's shared settings and its lock.</summary>
public interface IRoomSettingsService
{
	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The host or a manager.</param>
	/// <param name="change">The settings to change.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings, InvalidMods or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     Nothing changes unless every field is valid. Selecting or clearing the beatmap sets ready players back to not
	///     ready.
	///     Turning freemod on moves the room's mods that are not speed-changing onto each player; turning it off gives the
	///     room the host's mods. Changing the team type reassigns teams. Changing the mode drops mods, the room's and the
	///     players', that the new mode does not allow. Under freemod, a seated caller's mods that are not speed-changing
	///     become
	///     that caller's own mods. A change with no field set does nothing. Only the creator, a referee or a user with <see cref="Permissions.TournamentManageAnyRoom" /> can change
	///     whether the history is private.
	///     A change to the beatmap, mode, mods, freemod, team type or win condition cancels a countdown that would start the
	///     round.
	/// </remarks>
	Task<RoomResult> ConfigureAsync(Room room, Connection by, RoomSettingsChange change,
		CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="locked">The lock state to set.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized or RoomClosed when the room has closed; setting the current state again returns Ok and does
	///     nothing.
	/// </returns>
	Task<RoomResult> SetLockedAsync(Room room, Connection by, bool locked,
		CancellationToken cancellationToken = default);
}
