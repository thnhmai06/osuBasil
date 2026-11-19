using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomMembers
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomMembers(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<LobbyResult> JoinAsync(string password)
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.RequireJoin()?.ToLobby() ?? _scoped.Room.JoinClaiming(actor, password, _scoped.ManagesAnyRoom)
			: LobbyResult.NotInRoom);
	}

	public Task<RoomActionResult> LeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.Room.Leave(actor)
			: RoomActionResult.NotInRoom);
	}

	public async Task<LobbyResult> KickAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Staff) is { } denied) return denied.ToLobby();
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Kick(_scoped.Actor.Record, record).ToLobby()
			: LobbyResult.NotFound;
	}

	public async Task<LobbyResult> BanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied.ToLobby();
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Ban(_scoped.Actor.Record, record).ToLobby()
			: LobbyResult.NotFound;
	}

	public async Task<LobbyResult> UnbanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied.ToLobby();
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Unban(record).ToLobby()
			: LobbyResult.NotFound;
	}

	public async Task<LobbyResult> InviteAsync(User target)
	{
		if (_scoped.Require(RoomPrivileges.Player | RoomPrivileges.Moderators) is { } denied) return denied.ToLobby();
		if (!target.IsOnline) return LobbyResult.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.Actor.Record))
			return LobbyResult.Blocked;

		return _scoped.Room.Invite(_scoped.Actor.Record, target);
	}

	public Task<LobbyResult> ObserverJoinAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverJoin(actor)
			: LobbyResult.NotObserver);
	}

	public Task<LobbyResult> ObserverLeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverLeave(actor)
			: LobbyResult.NotObserver);
	}
}