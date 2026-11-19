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

	public async Task<LobbyResult> AddRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.Creator) is { } denied) return denied.ToLobby();
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.AddReferee(_scoped.Actor.Record, record).ToLobby()
			: LobbyResult.NotFound;
	}

	public async Task<LobbyResult> RemoveRefereeAsync(UserReference user)
	{
		if (_scoped.Require(RoomPrivileges.Creator) is { } denied) return denied.ToLobby();
		return await _scoped.Room.Context.Users.FindAsync(user) is { } record
			? _scoped.Room.RemoveReferee(_scoped.Actor.Record, record).ToLobby()
			: LobbyResult.NotFound;
	}

	public Task<RoomActionResult> SetHostAsync(StableSession? host)
	{
		return Task.FromResult(_scoped.Require(RoomPrivileges.Staff) ??
		                       _scoped.Room.SetHost(_scoped.Actor.Record, host));
	}
}