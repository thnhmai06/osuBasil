using Basil.Common.Application;
using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Users;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Joining, seating, removing and banning players, and inviting users, as the actor's transitions.</content>
public sealed partial class Room
{
	/// <summary>Seats a connection that asked to join, after the room's own checks.</summary>
	/// <param name="by">The connection joining.</param>
	/// <param name="password">The password the player supplied.</param>
	/// <param name="joinDenied">The denial for a player who may not join rooms, or <see langword="null" />.</param>
	/// <param name="managesAnyRoom">Whether the player manages every room, so the password does not apply.</param>
	/// <returns>
	///     Ok, AlreadySeated, Banned, a denial, InAnotherRoom, IsObserver, WrongPassword, Full or RoomClosed.
	/// </returns>
	internal Result<RoomOutcome> Join(BanchoConnection by, string password, Result<RoomOutcome>? joinDenied,
		bool managesAnyRoom)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			var stale = Slots.Find(by.Record)?.Player;
			if (stale is not null && (ReferenceEquals(stale, by) || stale.IsOpen)) return RoomOutcome.AlreadySeated;

			if (Members.Banned.Contains(by.Record)) return RoomOutcome.Banned;
			if (joinDenied is { } denied) return denied;

			if (RoomOf(by) is { } other && !ReferenceEquals(other, this)) return RoomOutcome.InAnotherRoom;
			if (Members.Observers.Any(observer => observer.Record.Equals(by.Record))) return RoomOutcome.IsObserver;
			if (!string.IsNullOrEmpty(Settings.Password) && Settings.Password != password && !managesAnyRoom)
				return RoomOutcome.WrongPassword;

			return TakeSeat(by, stale);
		}
	}

	/// <summary>Seats a connection on behalf of a manager, once the manager's rights were checked.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <returns>Ok, AlreadySeated, Banned, InAnotherRoom, IsObserver, Full or RoomClosed.</returns>
	internal Result<RoomOutcome> SeatPlayer(BanchoConnection player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			var stale = Slots.Find(player.Record)?.Player;
			if (stale is not null && (ReferenceEquals(stale, player) || stale.IsOpen))
				return RoomOutcome.AlreadySeated;
			if (Members.Banned.Contains(player.Record)) return RoomOutcome.Banned;
			if (RoomOf(player) is { } current && !ReferenceEquals(current, this)) return RoomOutcome.InAnotherRoom;
			if (Members.Observers.Any(observer => observer.Record.Equals(player.Record)))
				return RoomOutcome.IsObserver;

			return TakeSeat(player, stale);
		}
	}

	/// <summary>Checks what a manager's seating of a player needs and takes the player out of the room they sit in now.</summary>
	/// <param name="player">The player to seat here.</param>
	/// <param name="by">The manager seating the player.</param>
	/// <returns>
	///     <see langword="null" /> when the player may be seated; otherwise Banned, InAnotherRoom (the manager has no
	///     seating rights there), Full or IsObserver.
	/// </returns>
	/// <remarks>
	///     The player leaves the other room before this one seats them, and the two steps are separate: when this
	///     method returns, the player is in no room and seating them here is still to do.
	/// </remarks>
	internal Result<RoomOutcome>? MakeRoomFor(BanchoConnection player, Connection by)
	{
		if (Members.Banned.Contains(player.Record)) return RoomOutcome.Banned;
		if (RoomOf(player) is not { } other || ReferenceEquals(other, this)) return null;

		if (other.ScopeTo(by).Require(RoomPermissions.MembersSeat) is not null) return RoomOutcome.InAnotherRoom;
		if (!Slots.Any(slot => slot is { Locked: false, Player: null })) return RoomOutcome.Full;
		if (Members.Observers.Any(observer => observer.Record.Equals(player.Record))) return RoomOutcome.IsObserver;

		other.Leave(player);
		return null;
	}

	/// <summary>Removes a seated player on their own request.</summary>
	/// <param name="by">The player's game connection.</param>
	/// <returns>Ok, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Leave(BanchoConnection by)
	{
		lock (_sync)
		{
			return IsClosed ? RoomOutcome.RoomClosed : LeaveSeat(by);
		}
	}

	/// <summary>Removes a seated player on a manager's order.</summary>
	/// <param name="by">The manager.</param>
	/// <param name="player">The user to remove.</param>
	/// <returns>Ok, IsManager, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Kick(UserRecord by, UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.IsManagedBy(player)) return RoomOutcome.IsManager;
			if (Slots.Find(player) is not { Player: { } seated }) return RoomOutcome.NotInRoom;

			var slot = Vacate(seated)!;
			var progress = AdvanceRound();
			Emit(new RoomPlayerKicked(this, by, seated, slot.Index, Authority.Host, progress));
			ReportIfEmpty();
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Bans a user from the room and removes them if seated.</summary>
	/// <param name="by">The manager.</param>
	/// <param name="player">The user to ban.</param>
	/// <returns>Ok, IsManager or RoomClosed; banning a banned user again returns Ok.</returns>
	internal Result<RoomOutcome> Ban(UserRecord by, UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.IsManagedBy(player)) return RoomOutcome.IsManager;
			if (!Members.MutableBanned.Add(player)) return RoomOutcome.Ok;

			int? vacated = null;
			BanchoConnection? evicted = null;
			RoomRoundProgress? progress = null;
			if (Slots.Find(player) is { Player: { } seated })
			{
				evicted = seated;
				vacated = Vacate(seated)?.Index;
				progress = AdvanceRound();
			}

			Emit(new RoomPlayerBanned(this, by, player, vacated, evicted, Authority.Host, progress));
			if (evicted is not null)
			{
				ReportIfEmpty();
			}

			return RoomOutcome.Ok;
		}
	}

	/// <summary>Lifts a user's ban from the room.</summary>
	/// <param name="player">The user to unban.</param>
	/// <returns>Ok, NotBanned or RoomClosed.</returns>
	internal Result<RoomOutcome> Unban(UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableBanned.Remove(player)) return RoomOutcome.NotBanned;

			Emit(new RoomPlayerUnbanned(this, player));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Invites an online user to the room.</summary>
	/// <param name="by">The manager inviting.</param>
	/// <param name="target">The invited user.</param>
	/// <returns>Ok, TargetOffline, AlreadyInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Invite(UserRecord by, User target)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return RoomOutcome.TargetOffline;
			if (Slots.Find(target.Record) is not null) return RoomOutcome.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, by, target.Record));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Adds a tournament connection as an observer.</summary>
	/// <param name="by">The tournament connection observing.</param>
	/// <returns>Ok, IsPlayer or RoomClosed.</returns>
	internal Result<RoomOutcome> ObserverJoin(TourneyConnection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(by.Record) is not null) return RoomOutcome.IsPlayer;
			if (!Members.MutableObservers.Add(by)) return RoomOutcome.Ok;

			Emit(new RoomObserverJoined(this, by));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Removes a tournament connection from the room's observers.</summary>
	/// <param name="by">The tournament connection leaving.</param>
	/// <returns>Ok, NotObserver or RoomClosed.</returns>
	internal Result<RoomOutcome> ObserverLeave(TourneyConnection by)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableObservers.Remove(by)) return RoomOutcome.NotObserver;

			Emit(new RoomObserverLeft(this, by));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Closes the room, evicting its players and ending its round.</summary>
	/// <param name="by">The user who closes the room.</param>
	/// <returns>Ok; a room that has closed already returns Ok and does nothing.</returns>
	internal Result<RoomOutcome> Close(UserRecord by)
	{
		lock (_sync)
		{
			if (!IsClosed) Context.Lobby.Close(this, by);
			return RoomOutcome.Ok;
		}
	}
}