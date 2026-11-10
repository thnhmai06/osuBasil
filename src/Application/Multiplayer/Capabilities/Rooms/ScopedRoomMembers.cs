using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Membership operations on a room, scoped to a connection with its permissions.</summary>
public readonly struct ScopedRoomMembers
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped membership service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomMembers(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Seats the caller in the room.</summary>
	/// <param name="password">The password supplied by the player.</param>
	/// <returns>
	///     Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword, Full or
	///     RoomClosed.
	/// </returns>
	public Task<Result<RoomOutcome>> JoinAsync(string password)
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Actor is StableConnection actor
			? _scoped.RequireJoin() ?? _scoped.Room.JoinClaiming(actor, password, _scoped.ManagesAnyRoom)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Removes the caller from the room.</summary>
	/// <returns>Ok, NotInRoom or RoomClosed; leaving again returns NotInRoom.</returns>
	public Task<Result<RoomOutcome>> LeaveAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Actor is StableConnection actor
			? _scoped.Room.Leave(actor)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Removes a player from the room.</summary>
	/// <param name="player">The user to remove.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager, NotInRoom or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> KickAsync(UserRecord player)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersKick) ??
		                       _scoped.Room.Kick(_scoped.Actor.Record, player));
	}

	/// <summary>Bans a user from the room and removes them if seated.</summary>
	/// <param name="player">The user to ban.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager or RoomClosed; banning a banned user again returns Ok.</returns>
	public Task<Result<RoomOutcome>> BanAsync(UserRecord player)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersBan) ??
		                       _scoped.Room.Ban(_scoped.Actor.Record, player));
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="player">The user to unban.</param>
	/// <returns>Ok, NotAuthorized, Silenced, NotBanned or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> UnbanAsync(UserRecord player)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersBan) ??
		                       _scoped.Room.Unban(player));
	}

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="target">The invited user.</param>
	/// <returns>Ok, NotAuthorized, Silenced, Blocked, TargetOffline, AlreadyInRoom or RoomClosed.</returns>
	public async Task<Result<RoomOutcome>> InviteAsync(User target)
	{
		if (_scoped.Require(RoomPermissions.MembersInvite) is { } denied) return denied;
		if (!target.IsOnline) return RoomOutcome.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.Actor.Record))
			return RoomOutcome.Blocked;

		return _scoped.Room.Invite(_scoped.Actor.Record, target);
	}

	/// <summary>Adds the caller's tournament connection as an observer.</summary>
	/// <returns>Ok, IsPlayer or RoomClosed when called by a tournament connection; otherwise NotObserver.</returns>
	public Task<Result<RoomOutcome>> ObserverJoinAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Actor is TourneyConnection actor
			? _scoped.Room.ObserverJoin(actor)
			: RoomOutcome.NotObserver);
	}

	/// <summary>Removes the caller's tournament connection from the room's observers.</summary>
	/// <returns>Ok, NotObserver or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> ObserverLeaveAsync()
	{
		return Task.FromResult<Result<RoomOutcome>>(_scoped.Actor is TourneyConnection actor
			? _scoped.Room.ObserverLeave(actor)
			: RoomOutcome.NotObserver);
	}
}