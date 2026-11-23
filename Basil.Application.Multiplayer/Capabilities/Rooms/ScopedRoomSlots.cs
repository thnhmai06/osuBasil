using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Room;

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

	public async Task<RoomTargetResult> MoveAsync(int playerId, int index)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(playerId) is { } record
			? RoomTargetResults.Of(_scoped.Room.MovePlayer(record, index))
			: RoomTargetResult.NotFound;
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

	public async Task<RoomTargetResult> SetTeamAsync(int playerId, GameTeam team)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(playerId) is { } record
			? RoomTargetResults.Of(_scoped.Room.SetTeam(record, team))
			: RoomTargetResult.NotFound;
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