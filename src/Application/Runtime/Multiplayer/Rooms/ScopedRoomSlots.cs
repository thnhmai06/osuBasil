using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

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
	public async Task<Result<RoomOutcome>> ChangeAsync(int index, CancellationToken cancellationToken = default)
	{
		var by = _scoped.By;
		if (by is not BanchoConnection bancho) return RoomOutcome.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(bancho) is not { } from) return RoomOutcome.NotInRoom;
		if (room.Slots.Locked) return RoomOutcome.RoomLocked;
		if (room.Rounds.InProgress) return RoomOutcome.InProgress;
		if (room.Slots.At(index) is not { } to) return RoomOutcome.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomOutcome.Ok;
		if (to.Locked || to.Player is not null) return RoomOutcome.SlotNotOpen;

		from.MoveTo(to);
		room.Context.Events.Publish(new RoomPlayerMoved(room, bancho, from.Index, to.Index));
		return RoomOutcome.Ok;
	}

	/// <summary>Moves a player to an empty, unlocked slot.</summary>
	/// <param name="player">The user to act on.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NotInRoom, SlotNotOpen or RoomClosed when the room has closed.</returns>
	/// <remarks>This is a referee operation, so it is allowed while the room is locked.</remarks>
	public async Task<Result<RoomOutcome>> MoveAsync(UserRecord player, int index, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.SlotsMovePlayers) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(player) is not { Player: { } seated } from) return RoomOutcome.NotInRoom;
		if (room.Slots.At(index) is not { } to) return RoomOutcome.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomOutcome.Ok;
		if (to.Locked || to.Player is not null) return RoomOutcome.SlotNotOpen;

		from.MoveTo(to);
		room.Context.Events.Publish(new RoomPlayerMoved(room, seated, from.Index, to.Index));
		return RoomOutcome.Ok;
	}

	/// <summary>Arranges every slot of a room at once.</summary>
	/// <param name="arrangement">The slots to set; slots not listed end up empty and keep their lock.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings or RoomClosed.</returns>
	/// <remarks>
	///     Every seated player must appear exactly once, slot numbers must be distinct and from 1 to 16, and a slot
	///     with a player cannot be locked. A team is applied only while the room plays in teams.
	/// </remarks>
	public async Task<Result<RoomOutcome>> ArrangeAsync(IReadOnlyList<SlotArrangement> arrangement,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.SlotsMovePlayers) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Rounds.InProgress) return RoomOutcome.InProgress;

		var seated = room.Slots.Where(s => s.Player is not null)
			.ToDictionary(s => s.Player!.Record, s => s.Capture());
		var named = arrangement.Where(e => e.Player is not null).Select(e => e.Player!).ToList();
		if (arrangement.Any(e => e.Index is < 1 or > RoomSlots.MaxSlotCount || (e.Player is not null && e.Locked)) ||
		    arrangement.DistinctBy(e => e.Index).Count() != arrangement.Count ||
		    named.Count != seated.Count || !seated.Keys.ToHashSet().SetEquals(named))
			return RoomOutcome.InvalidSettings;

		foreach (var slot in room.Slots)
			slot.Clear();

		foreach (var entry in arrangement)
		{
			var slot = room.Slots.At(entry.Index)!;
			slot.Locked = entry.Locked;
			if (entry.Player is null) continue;

			slot.Restore(seated[entry.Player]);
			if (entry.Team is { } team && room.Settings.MatchSettings.TeamType.IsTeamMode()) slot.Team = team;
		}

		room.Context.Events.Publish(new RoomSlotsArranged(room));
		return RoomOutcome.Ok;
	}

	/// <summary>Marks the caller ready or not ready.</summary>
	/// <param name="ready">Whether the caller is ready to play.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress or RoomClosed when the room has closed.</returns>
	public async Task<Result<RoomOutcome>> SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
	{
		var by = _scoped.By;
		if (by is not BanchoConnection bancho) return RoomOutcome.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
		if (room.Rounds.InProgress) return RoomOutcome.InProgress;

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return RoomOutcome.Ok;

		slot.SetStatus(status);
		room.Context.Events.Publish(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomOutcome.Ok;
	}

	/// <summary>Reports whether the caller has the selected beatmap.</summary>
	/// <param name="has">Whether the caller has the selected beatmap.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     The report is ignored while the caller is playing; having the map changes nothing unless the caller had reported
	///     not having it.
	/// </remarks>
	public async Task<Result<RoomOutcome>> SetHasMapAsync(bool has, CancellationToken cancellationToken = default)
	{
		var by = _scoped.By;
		if (by is not BanchoConnection bancho) return RoomOutcome.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
		if (slot.Status is RoomSlotStatus.Playing || (has && slot.Status is not RoomSlotStatus.NoMap))
			return RoomOutcome.Ok;

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return RoomOutcome.Ok;

		slot.SetStatus(status);
		room.Context.Events.Publish(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomOutcome.Ok;
	}

	/// <summary>Switches the caller to the other team.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked, InProgress or RoomClosed when the room has closed.</returns>
	public async Task<Result<RoomOutcome>> ToggleTeamAsync(CancellationToken cancellationToken = default)
	{
		var by = _scoped.By;
		if (by is not BanchoConnection bancho) return RoomOutcome.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
		if (!room.Settings.MatchSettings.TeamType.IsTeamMode()) return RoomOutcome.NoTeams;
		if (room.Slots.Locked) return RoomOutcome.RoomLocked;
		if (room.Rounds.InProgress) return RoomOutcome.InProgress;

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.Team = team;
		room.Context.Events.Publish(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomOutcome.Ok;
	}

	/// <summary>Puts a player on a team.</summary>
	/// <param name="player">The user to act on.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, NoTeams, NotInRoom or RoomClosed when the room has closed.</returns>
	public async Task<Result<RoomOutcome>> SetTeamAsync(UserRecord player, GameTeam team,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.SlotsMovePlayers) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (!room.Settings.MatchSettings.TeamType.IsTeamMode()) return RoomOutcome.NoTeams;
		if (room.Slots.Find(player) is not { Player: not null } slot) return RoomOutcome.NotInRoom;
		if (slot.Team == team) return RoomOutcome.Ok;

		slot.Team = team;
		room.Context.Events.Publish(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomOutcome.Ok;
	}

	/// <summary>Chooses the caller's own mods while freemod is on.</summary>
	/// <param name="mods">The mods to select.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed, InvalidMods or RoomClosed when the room has closed.</returns>
	public async Task<Result<RoomOutcome>> SetModsAsync(GameMods mods, CancellationToken cancellationToken = default)
	{
		var by = _scoped.By;
		if (by is not BanchoConnection bancho) return RoomOutcome.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
		if (room.Rounds.InProgress) return RoomOutcome.InProgress;
		if (!room.Settings.MatchSettings.Freemods) return RoomOutcome.NotFreemod;
		if ((mods & GameModsGroups.SpeedChanging) != GameMods.NoMod) return RoomOutcome.SpeedModNotAllowed;
		if (!mods.IsValid(room.Settings.MatchSettings.Mode)) return RoomOutcome.InvalidMods;
		if (slot.Mods == mods) return RoomOutcome.Ok;

		slot.Mods = mods;
		room.Context.Events.Publish(new RoomSlotModsChanged(room, slot.Index, mods));
		return RoomOutcome.Ok;
	}

	/// <summary>Toggles whether a slot is locked.</summary>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen, OwnSlot or RoomClosed when the room has closed.</returns>
	public async Task<Result<RoomOutcome>> ToggleLockAsync(int index, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.SlotsLock) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomOutcome.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.At(index) is not { } slot) return RoomOutcome.SlotNotOpen;
		if (ReferenceEquals(slot.Player, _scoped.By)) return RoomOutcome.OwnSlot;

		var locking = !slot.Locked;

		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			room.Vacate(player);
			progress = room.AdvanceRound();
		}

		slot.Locked = locking;
		room.Context.Events.Publish(new RoomSlotLockChanged(room, slot.Index, locking, evicted, room.Authority.Host,
			progress));

		if (evicted is not null)
		{
			room.LeaveChannel(evicted);
			room.ReportIfEmpty();
		}

		return RoomOutcome.Ok;
	}
}