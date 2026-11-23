using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Sessions.Objects;
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

	public Task<RoomJoinResult> JoinAsync(string password)
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.RequireJoin() is null
				? _scoped.Room.JoinClaiming(actor, password, _scoped.ManagesAnyRoom)
				: RoomJoinResult.Denied
			: RoomJoinResult.NotInRoom);
	}

	public Task<RoomActionResult> LeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.Room.Leave(actor)
			: RoomActionResult.NotInRoom);
	}

	public async Task<RoomTargetResult> KickAsync(int playerId)
	{
		if (_scoped.Require(RoomPrivileges.Staff) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(playerId) is { } record
			? RoomTargetResults.Of(_scoped.Room.Kick(_scoped.Actor.Record, record))
			: RoomTargetResult.NotFound;
	}

	public async Task<RoomTargetResult> BanAsync(int playerId)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(playerId) is { } record
			? RoomTargetResults.Of(_scoped.Room.Ban(_scoped.Actor.Record, record))
			: RoomTargetResult.NotFound;
	}

	public async Task<RoomTargetResult> UnbanAsync(int playerId)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is not null) return RoomTargetResult.Denied;
		return await _scoped.Room.Context.Users.FindAsync(playerId) is { } record
			? RoomTargetResults.Of(_scoped.Room.Unban(record))
			: RoomTargetResult.NotFound;
	}

	public async Task<RoomInviteResult> InviteAsync(UserSessions target)
	{
		if (_scoped.Require(RoomPrivileges.Player | RoomPrivileges.Moderators) is not null)
			return RoomInviteResult.Denied;
		if (!target.IsOnline) return RoomInviteResult.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.Actor.Record))
			return RoomInviteResult.Blocked;

		return _scoped.Room.Invite(_scoped.Actor.Record, target);
	}

	public Task<RoomJoinResult> ObserverJoinAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverJoin(actor)
			: RoomJoinResult.NotObserver);
	}

	public Task<RoomJoinResult> ObserverLeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverLeave(actor)
			: RoomJoinResult.NotObserver);
	}
}