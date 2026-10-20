using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Domain.Multiplayer;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

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
	/// <returns>Ok, AlreadySeated, Banned, Silenced, NotAuthorized, InAnotherRoom, IsObserver, WrongPassword, Full or RoomClosed.</returns>
	public async Task<RoomResult> JoinAsync(string password, CancellationToken cancellationToken = default)
	{
		if (_scoped.By is not BanchoConnection by) return RoomResult.NotInRoom;

		var room = _scoped.Room;
		await using var scope = await room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var stale = room.Slots.Find(by.Record)?.Player;
		if (stale is not null && (ReferenceEquals(stale, by) || stale.IsOpen)) return RoomResult.AlreadySeated;

		if (room.Members.Banned.Contains(by.Record)) return RoomResult.Banned;
		if (_scoped.RequireJoin() is { } denied) return denied;

		if (room.RoomOf(by) is { } other && !ReferenceEquals(other, room)) return RoomResult.InAnotherRoom;
		if (room.Members.Observers.Any(observer => observer.Record.Equals(by.Record))) return RoomResult.IsObserver;
		if (!string.IsNullOrEmpty(room.Settings.Password) && room.Settings.Password != password &&
		    !_scoped.ManagesAnyRoom)
			return RoomResult.WrongPassword;

		return room.TakeSeat(by, stale);
	}

	/// <summary>Removes the caller from the room.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotInRoom or RoomClosed; leaving again returns NotInRoom.</returns>
	public async Task<RoomResult> LeaveAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.By is not BanchoConnection by) return RoomResult.NotInRoom;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		return scope is null ? RoomResult.RoomClosed : _scoped.Room.Leave(by);
	}

	/// <summary>Seats an online player in the room on behalf of a manager.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, TargetOffline, AlreadySeated, Banned, Silenced, InAnotherRoom, IsObserver, Full or RoomClosed.</returns>
	public async Task<RoomResult> SeatAsync(BanchoConnection player, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersSeat) is { } denied) return denied;
		if (!player.IsOpen) return RoomResult.TargetOffline;

		switch (player.User.Check(Permissions.RoomJoin, _scoped.Room.Context.Clock.GetUtcNow()))
		{
			case Access.NotGranted: return RoomResult.NotAuthorized;
			case Access.Suspended: return RoomResult.Silenced;
			case Access.Allowed: break;
		}

		var room = _scoped.Room;
		if (room.Members.Banned.Contains(player.Record)) return RoomResult.Banned;

		if (room.RoomOf(player) is { } other && !ReferenceEquals(other, room))
		{
			if (other.ScopeTo(_scoped.By).Require(RoomPermissions.MembersSeat) is not null)
				return RoomResult.InAnotherRoom;
			if (!room.Slots.Any(slot => slot is { Locked: false, Player: null })) return RoomResult.Full;
			if (room.Members.Observers.Any(observer => observer.Record.Equals(player.Record)))
				return RoomResult.IsObserver;
			await other.ScopeTo(player).Members.LeaveAsync(cancellationToken);
		}

		await using var scope = await room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var stale = room.Slots.Find(player.Record)?.Player;
		if (stale is not null && (ReferenceEquals(stale, player) || stale.IsOpen)) return RoomResult.AlreadySeated;
		if (room.Members.Banned.Contains(player.Record)) return RoomResult.Banned;
		if (room.RoomOf(player) is { } current && !ReferenceEquals(current, room)) return RoomResult.InAnotherRoom;
		if (room.Members.Observers.Any(observer => observer.Record.Equals(player.Record))) return RoomResult.IsObserver;

		return room.TakeSeat(player, stale);
	}

	/// <summary>Removes a player from the room.</summary>
	/// <param name="player">The user to remove.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager, NotInRoom or RoomClosed.</returns>
	public async Task<RoomResult> KickAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersKick) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		var by = _scoped.By;
		if (room.Authority.IsManagedBy(player)) return RoomResult.IsManager;
		if (room.Slots.Find(player) is not { Player: { } seated }) return RoomResult.NotInRoom;

		var slot = room.Vacate(seated)!;
		var progress = room.AdvanceRound();
		room.Context.Events.Publish(new RoomPlayerKicked(room, by.Record, seated, slot.Index, room.Authority.Host,
			progress));
		room.LeaveChannel(seated);
		room.ReportIfEmpty();
		return RoomResult.Ok;
	}

	/// <summary>Bans a user from the room and removes them if seated.</summary>
	/// <param name="player">The user to ban.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, IsManager or RoomClosed; banning a banned user again returns Ok.</returns>
	public async Task<RoomResult> BanAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersBan) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		var by = _scoped.By;
		if (room.Authority.IsManagedBy(player)) return RoomResult.IsManager;
		if (!room.Members.MutableBanned.Add(player)) return RoomResult.Ok;

		int? vacated = null;
		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (room.Slots.Find(player) is { Player: { } seated })
		{
			evicted = seated;
			vacated = room.Vacate(seated)?.Index;
			progress = room.AdvanceRound();
		}

		room.Context.Events.Publish(new RoomPlayerBanned(room, by.Record, player, vacated, evicted, room.Authority.Host,
			progress));
		if (evicted is not null)
		{
			room.LeaveChannel(evicted);
			room.ReportIfEmpty();
		}

		return RoomResult.Ok;
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="player">The user to unban.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, NotBanned or RoomClosed.</returns>
	public async Task<RoomResult> UnbanAsync(UserRecord player, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersBan) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!room.Members.MutableBanned.Remove(player)) return RoomResult.NotBanned;

		room.Context.Events.Publish(new RoomPlayerUnbanned(room, player));
		return RoomResult.Ok;
	}

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="target">The invited user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, Silenced, Blocked, TargetOffline, AlreadyInRoom or RoomClosed.</returns>
	public async Task<RoomResult> InviteAsync(User target, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.MembersInvite) is { } denied) return denied;
		if (!target.IsOnline) return RoomResult.TargetOffline;
		if (!_scoped.ManagesAnyRoom &&
		    await _scoped.Room.Context.Users.BlocksAsync(target.Record, _scoped.By.Record, cancellationToken))
			return RoomResult.Blocked;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (!target.Connections.Any(connection => connection.IsOpen)) return RoomResult.TargetOffline;
		if (room.Slots.Find(target.Record) is not null) return RoomResult.AlreadyInRoom;

		room.Context.Events.Publish(new RoomPlayerInvited(room, _scoped.By.Record, target.Record));
		return RoomResult.Ok;
	}

	/// <summary>Adds the caller's tournament connection as an observer.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, IsPlayer or RoomClosed when called by a tournament connection; otherwise NotObserver.</returns>
	public async Task<RoomResult> ObserverJoinAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.By is not TourneyConnection by) return RoomResult.NotObserver;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		return scope is not null ? _scoped.Room.ObserverJoin(by) : RoomResult.RoomClosed;
	}

	/// <summary>Removes the caller's tournament connection from the room's observers.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotObserver or RoomClosed.</returns>
	public async Task<RoomResult> ObserverLeaveAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.By is not TourneyConnection by) return RoomResult.NotObserver;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		return scope is not null ? _scoped.Room.ObserverLeave(by) : RoomResult.RoomClosed;
	}
}