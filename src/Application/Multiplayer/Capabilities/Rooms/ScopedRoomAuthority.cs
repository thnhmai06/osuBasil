using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomAuthority
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomAuthority(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public async Task<RoomOutcome> AddRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.RefereesManage) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.AddReferee(_scoped.Actor.Record, record)
			: RoomOutcome.NotFound;
	}

	public async Task<RoomOutcome> RemoveRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.RefereesManage) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.RemoveReferee(_scoped.Actor.Record, record)
			: RoomOutcome.NotFound;
	}

	public Task<RoomOutcome> SetHostAsync(StableSession? host)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.HostTransfer) ??
		                       _scoped.Room.SetHost(_scoped.Actor.Record, host));
	}
}