using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

public readonly struct ScopedRoomMembers
{
	private readonly ScopedRoom _scoped;

	internal ScopedRoomMembers(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	public Task<RoomOutcome> JoinAsync(string password)
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.RequireJoin() ?? _scoped.Room.JoinClaiming(actor, password, _scoped.ManagesAnyRoom)
			: RoomOutcome.NotInRoom);
	}

	public Task<RoomOutcome> LeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.Room.Leave(actor)
			: RoomOutcome.NotInRoom);
	}

	public async Task<RoomOutcome> KickAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.MembersKick) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Kick(_scoped.Actor.Record, record)
			: RoomOutcome.NotFound;
	}

	public async Task<RoomOutcome> BanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.MembersBan) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Ban(_scoped.Actor.Record, record)
			: RoomOutcome.NotFound;
	}

	public async Task<RoomOutcome> UnbanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.MembersBan) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Unban(record)
			: RoomOutcome.NotFound;
	}

	public async Task<RoomOutcome> InviteAsync(User target)
	{
		if (_scoped.Require(RoomPrivileges.MembersInvite) is { } denied) return denied;
		if (!target.IsOnline) return RoomOutcome.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.Actor.Record))
			return RoomOutcome.Blocked;

		return _scoped.Room.Invite(_scoped.Actor.Record, target);
	}

	public Task<RoomOutcome> ObserverJoinAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverJoin(actor)
			: RoomOutcome.NotObserver);
	}

	public Task<RoomOutcome> ObserverLeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverLeave(actor)
			: RoomOutcome.NotObserver);
	}
}