using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomAuthority
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomAuthority(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public async Task<RoomTargetResult> AddRefereeAsync(int userId)
	{
		if (_scoped.Require(RoomPrivileges.Creator) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(userId) is { } record
			? RoomTargetResults.Of(_scoped.Room.AddReferee(_scoped.Actor.Record, record))
			: RoomTargetResult.NotFound;
	}

	public async Task<RoomTargetResult> RemoveRefereeAsync(int userId)
	{
		if (_scoped.Require(RoomPrivileges.Creator) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(userId) is { } record
			? RoomTargetResults.Of(_scoped.Room.RemoveReferee(_scoped.Actor.Record, record))
			: RoomTargetResult.NotFound;
	}

	public Task<RoomActionResult> SetHostAsync(StableSession? host)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Staff) ??
		                       _scoped.Room.SetHost(_scoped.Actor.Record, host));
	}
}