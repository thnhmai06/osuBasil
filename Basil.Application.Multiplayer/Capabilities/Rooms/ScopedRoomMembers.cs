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

	public Task<RoomActionResult> JoinAsync(string password)
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.RequireJoin() ?? _scoped.Room.JoinClaiming(actor, password, _scoped.ManagesAnyRoom)
			: RoomActionResult.NotInRoom);
	}

	public Task<RoomActionResult> LeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is StableSession actor
			? _scoped.Room.Leave(actor)
			: RoomActionResult.NotInRoom);
	}

	public async Task<RoomActionResult> KickAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Staff) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Kick(_scoped.Actor.Record, record)
			: RoomActionResult.NotFound;
	}

	public async Task<RoomActionResult> BanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Ban(_scoped.Actor.Record, record)
			: RoomActionResult.NotFound;
	}

	public async Task<RoomActionResult> UnbanAsync(UserReference player)
	{
		if (_scoped.Require(RoomPrivileges.Moderators) is { } denied) return denied;
		return await _scoped.Room.Context.Users.FindAsync(player) is { } record
			? _scoped.Room.Unban(record)
			: RoomActionResult.NotFound;
	}

	public async Task<RoomActionResult> InviteAsync(User target)
	{
		if (_scoped.Require(RoomPrivileges.Player | RoomPrivileges.Moderators) is { } denied) return denied;
		if (!target.IsOnline) return RoomActionResult.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.Actor.Record))
			return RoomActionResult.Blocked;

		return _scoped.Room.Invite(_scoped.Actor.Record, target);
	}

	public Task<RoomActionResult> ObserverJoinAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverJoin(actor)
			: RoomActionResult.NotObserver);
	}

	public Task<RoomActionResult> ObserverLeaveAsync()
	{
		return Task.FromResult(_scoped.Actor is TourneySession actor
			? _scoped.Room.ObserverLeave(actor)
			: RoomActionResult.NotObserver);
	}
}