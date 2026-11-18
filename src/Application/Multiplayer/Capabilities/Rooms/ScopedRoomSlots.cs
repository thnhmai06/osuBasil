using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomSlots
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomSlots(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomOutcome> ChangeAsync(int index)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.ChangeSlot(stable, index)
			: RoomOutcome.NotInRoom);
	}

	public async Task<RoomOutcome> MoveAsync(UserReference player, int index)
	{
		if (_scoped.Require(RoomPrivileges.SlotsMovePlayers) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.MovePlayer(record, index)
			: RoomOutcome.NotFound;
	}

	public Task<RoomOutcome> ArrangeAsync(IReadOnlyList<SlotArrangement> arrangement)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.SlotsMovePlayers) ??
		                       _scoped.Room.ArrangeSlots(arrangement));
	}

	public Task<RoomOutcome> SetReadyAsync(bool ready)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetReady(stable, ready)
			: RoomOutcome.NotInRoom);
	}

	public Task<RoomOutcome> SetHasMapAsync(bool has)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetHasMap(stable, has)
			: RoomOutcome.NotInRoom);
	}

	public Task<RoomOutcome> ToggleTeamAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.ToggleTeam(stable)
			: RoomOutcome.NotInRoom);
	}

	public async Task<RoomOutcome> SetTeamAsync(UserReference player, GameTeam team)
	{
		if (_scoped.Require(RoomPrivileges.SlotsMovePlayers) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.SetTeam(record, team)
			: RoomOutcome.NotFound;
	}

	public Task<RoomOutcome> SetModsAsync(GameMods mods)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetMods(stable, mods)
			: RoomOutcome.NotInRoom);
	}

	public Task<RoomOutcome> ToggleLockAsync(int index)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.SlotsLock) ??
		                       _scoped.Room.ToggleSlotLock(_scoped.Actor, index));
	}
}