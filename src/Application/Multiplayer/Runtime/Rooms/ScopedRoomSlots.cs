using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Runtime.Rooms;

/// <summary>Slot operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomSlots
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped slots service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomSlots(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Moves the caller to another open slot.</summary>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, RoomLocked, InProgress, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>Moving to the caller's own slot returns Ok and does nothing.</remarks>
	public Task<Result<RoomOutcome>> ChangeAsync(int index, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection bancho
			? _scoped.Room.ChangeSlot(bancho, index)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Moves a player to an empty, unlocked slot.</summary>
	/// <param name="player">The user to act on.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>This is a referee operation, so it is allowed while the room is locked.</remarks>
	public Task<Result<RoomOutcome>> MoveAsync(UserRecord player, int index,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.SlotsMovePlayers) ??
		                       _scoped.Room.MovePlayer(player, index));
	}

	/// <summary>Arranges every slot of a room at once.</summary>
	/// <param name="arrangement">The slots to set; slots not listed end up empty and keep their lock.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings or RoomClosed.</returns>
	/// <remarks>
	///     Every seated player must appear exactly once, slot numbers must be distinct and from 1 to 16, and a slot
	///     with a player cannot be locked. A team is applied only while the room plays in teams.
	/// </remarks>
	public Task<Result<RoomOutcome>> ArrangeAsync(IReadOnlyList<SlotArrangement> arrangement,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.SlotsMovePlayers) ??
		                       _scoped.Room.ArrangeSlots(arrangement));
	}

	/// <summary>Marks the caller ready or not ready.</summary>
	/// <param name="ready">Whether the caller is ready to play.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection bancho
			? _scoped.Room.SetReady(bancho, ready)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Reports whether the caller has the selected beatmap.</summary>
	/// <param name="has">Whether the caller has the selected beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     The report is ignored while the caller is playing; having the map changes nothing unless the caller had reported
	///     not having it.
	/// </remarks>
	public Task<Result<RoomOutcome>> SetHasMapAsync(bool has, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection bancho
			? _scoped.Room.SetHasMap(bancho, has)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Switches the caller to the other team.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked, InProgress or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> ToggleTeamAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection bancho
			? _scoped.Room.ToggleTeam(bancho)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Puts a player on a team.</summary>
	/// <param name="player">The user to act on.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoTeams, NotInRoom or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> SetTeamAsync(UserRecord player, GameTeam team,
		CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.SlotsMovePlayers) ??
		                       _scoped.Room.SetTeam(player, team));
	}

	/// <summary>Chooses the caller's own mods while freemod is on.</summary>
	/// <param name="mods">The mods to select.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed, InvalidMods or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> SetModsAsync(GameMods mods, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection bancho
			? _scoped.Room.SetMods(bancho, mods)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Toggles whether a slot is locked.</summary>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen, OwnSlot or RoomClosed when the room has closed.</returns>
	public Task<Result<RoomOutcome>> ToggleLockAsync(int index, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.SlotsLock) ??
		                       _scoped.Room.ToggleSlotLock(_scoped.By, index));
	}
}