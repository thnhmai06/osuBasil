using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomSlots
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomSlots(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomActionResult> ChangeAsync(int index)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.ChangeSlot(stable, index)
			: RoomActionResult.NotInRoom);
	}

	public async Task<RoomActionResult> MoveAsync(UserReference player, int index)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.MovePlayer(record, index)
			: RoomActionResult.NotFound;
	}

	public Task<RoomActionResult> ArrangeAsync(IReadOnlyList<SlotArrangement> arrangement)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Moderators) ??
		                       _scoped.Room.ArrangeSlots(arrangement));
	}

	public Task<RoomActionResult> SetReadyAsync(bool ready)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetReady(stable, ready)
			: RoomActionResult.NotInRoom);
	}

	public Task<RoomActionResult> SetHasMapAsync(bool has)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetHasMap(stable, has)
			: RoomActionResult.NotInRoom);
	}

	public Task<RoomActionResult> ToggleTeamAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.ToggleTeam(stable)
			: RoomActionResult.NotInRoom);
	}

	public async Task<RoomActionResult> SetTeamAsync(UserReference player, GameTeam team)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.SetTeam(record, team)
			: RoomActionResult.NotFound;
	}

	public Task<RoomActionResult> SetModsAsync(GameMods mods)
	{
		return Task.FromResult(_scoped.Actor is StableSession stable
			? _scoped.Room.SetMods(stable, mods)
			: RoomActionResult.NotInRoom);
	}

	public Task<RoomActionResult> ToggleLockAsync(int index)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Staff) ??
		                       _scoped.Room.ToggleSlotLock(_scoped.Actor, index));
	}
}