using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Slots and what each player sets on their own slot.</summary>
public interface IRoomSlotsService
{
	/// <summary>Moves the caller to another open slot.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, RoomLocked, InProgress, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>Moving to the caller's own slot returns Ok and does nothing.</remarks>
	Task<RoomResult> ChangeSlotAsync(Room room, BanchoConnection by, int index,
		CancellationToken cancellationToken = default);

	/// <summary>Moves a player to an empty, unlocked slot.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>This is a referee operation, so it is allowed while the room is locked.</remarks>
	Task<RoomResult> MoveAsync(Room room, Connection by, User player, int index,
		CancellationToken cancellationToken = default);

	/// <summary>Arranges every slot of a room at once.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The creator, a referee or a user with <see cref="Permissions.TournamentManageAnyRoom" />.</param>
	/// <param name="arrangement">The slots to set; slots not listed end up empty and keep their lock.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings or RoomClosed.</returns>
	/// <remarks>
	///     Every seated player must appear exactly once, slot numbers must be distinct and from 1 to 16, and a slot
	///     with a player cannot be locked. A team is applied only while the room plays in teams.
	/// </remarks>
	Task<RoomResult> ArrangeSlotsAsync(Room room, Connection by, IReadOnlyList<SlotArrangement> arrangement,
		CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks a slot; locking an occupied slot removes its player.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen, OwnSlot or RoomClosed when the room has closed.</returns>
	/// <remarks>A player cannot lock the slot they occupy.</remarks>
	Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index,
		CancellationToken cancellationToken = default);

	/// <summary>Marks the caller ready or not ready.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="ready">Whether the caller is ready to play.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetReadyAsync(Room room, BanchoConnection by, bool ready,
		CancellationToken cancellationToken = default);

	/// <summary>Reports whether the caller has the selected beatmap.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="has">Whether the caller has the selected beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     The report is ignored while the caller is playing; having the map changes nothing unless the caller had reported
	///     not having it.
	/// </remarks>
	Task<RoomResult> SetHasMapAsync(Room room, BanchoConnection by, bool has,
		CancellationToken cancellationToken = default);

	/// <summary>Switches the caller to the other team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked, InProgress or RoomClosed when the room has closed.</returns>
	Task<RoomResult> ToggleTeamAsync(Room room, BanchoConnection by, CancellationToken cancellationToken = default);

	/// <summary>Puts a player on a team.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="player">The user to act on.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoTeams, NotInRoom or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetTeamAsync(Room room, Connection by, User player, GameTeam team,
		CancellationToken cancellationToken = default);

	/// <summary>Chooses the caller's own mods while freemod is on.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's game client.</param>
	/// <param name="mods">The mods to select.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed, InvalidMods or RoomClosed when the room has closed.</returns>
	Task<RoomResult> SetPlayerModsAsync(Room room, BanchoConnection by, GameMods mods,
		CancellationToken cancellationToken = default);
}
