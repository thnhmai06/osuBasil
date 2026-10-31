using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities.Rooms;

/// <summary>Membership operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomMembers
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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword, Full or
	///     RoomClosed.
	/// </returns>
	public Task<Result<RoomOutcome>> JoinAsync(string password, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection by
			? Claiming(by, () => _scoped.Room.Join(by, password, _scoped.RequireJoin(), _scoped.ManagesAnyRoom))
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Runs a seating of a player while the lobby knows that this room is about to seat them.</summary>
	/// <param name="player">The player to be seated.</param>
	/// <param name="seat">The seating.</param>
	/// <returns>InAnotherRoom when another room is seating the same player right now; otherwise the seating's result.</returns>
	private Result<RoomOutcome> Claiming(BanchoConnection player, Func<Result<RoomOutcome>> seat)
	{
		var room = _scoped.Room;
		var lobby = room.Context.Lobby;
		if (!lobby.TryClaim(player, room)) return RoomOutcome.InAnotherRoom;

		try
		{
			return seat();
		}
		finally
		{
			lobby.Release(player, room);
		}
	}

	/// <summary>Removes the caller from the room.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed; leaving again returns NotInRoom.</returns>
	public Task<Result<RoomOutcome>> LeaveAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is BanchoConnection by
			? _scoped.Room.Leave(by)
			: RoomOutcome.NotInRoom);
	}

	/// <summary>Seats an online player in the room on behalf of a manager.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, a denial naming the missing permission (also when the player may not join rooms), TargetOffline,
	///     AlreadySeated, Banned, InAnotherRoom, IsObserver, Full or RoomClosed.
	/// </returns>
	public Task<Result<RoomOutcome>> SeatAsync(BanchoConnection player,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersSeat) is { } denied) return Task.FromResult(denied);
		if (!player.IsOpen) return Task.FromResult<Result<RoomOutcome>>(RoomOutcome.TargetOffline);

		if (!player.User.EffectivePermissions(_scoped.Room.Context.Clock.GetUtcNow()).Allows(UserPermissions.RoomJoin))
			return Task.FromResult(Result<RoomOutcome>.Denied(UserPermissions.RoomJoin));

		return Task.FromResult(_scoped.Room.MakeRoomFor(player, _scoped.By) ??
		                       Claiming(player, () => _scoped.Room.SeatPlayer(player)));
	}

	/// <summary>Removes a player from the room.</summary>
	/// <param name="player">The user to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager, NotInRoom or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> KickAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersKick) ??
		                       _scoped.Room.Kick(_scoped.By.Record, player));
	}

	/// <summary>Bans a user from the room and removes them if seated.</summary>
	/// <param name="player">The user to ban.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager or RoomClosed; banning a banned user again returns Ok.</returns>
	public Task<Result<RoomOutcome>> BanAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersBan) ??
		                       _scoped.Room.Ban(_scoped.By.Record, player));
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="player">The user to unban.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, NotBanned or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> UnbanAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.Require(RoomPermissions.MembersBan) ??
		                       _scoped.Room.Unban(player));
	}

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="target">The invited user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, Blocked, TargetOffline, AlreadyInRoom or RoomClosed.</returns>
	public async Task<Result<RoomOutcome>> InviteAsync(User target, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersInvite) is { } denied) return denied;
		if (!target.IsOnline) return RoomOutcome.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.By.Record, cancellationToken))
			return RoomOutcome.Blocked;

		return _scoped.Room.Invite(_scoped.By.Record, target);
	}

	/// <summary>Adds the caller's tournament connection as an observer.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, IsPlayer or RoomClosed when called by a tournament connection; otherwise NotObserver.</returns>
	public Task<Result<RoomOutcome>> ObserverJoinAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is TourneyConnection by
			? _scoped.Room.ObserverJoin(by)
			: RoomOutcome.NotObserver);
	}

	/// <summary>Removes the caller's tournament connection from the room's observers.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotObserver or RoomClosed.</returns>
	public Task<Result<RoomOutcome>> ObserverLeaveAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_scoped.By is TourneyConnection by
			? _scoped.Room.ObserverLeave(by)
			: RoomOutcome.NotObserver);
	}
}