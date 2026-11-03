using Basil.Application.Common;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <content>Joining, seating, removing and banning players, and inviting users, as the actor's transitions.</content>
public sealed partial class Room
{
	/// <summary>Seats a connection that asked to join, after the room's own checks.</summary>
	/// <param name="actor">The connection joining.</param>
	/// <param name="password">The password the player supplied.</param>
	/// <returns>
	///     Ok, AlreadySeated, Banned, a denial naming the permission to join rooms, InAnotherRoom, IsObserver,
	///     WrongPassword, Full or RoomClosed.
	/// </returns>
	/// <remarks>
	///     What the actor may do is asked of the actor's connection, not handed in: a player who may not join rooms is
	///     refused, and one who manages every room does not need the password.
	/// </remarks>
	internal Result<RoomOutcome> Join(BanchoConnection actor, string password)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			var stale = Slots.Find(actor.Record)?.Player;
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen)) return RoomOutcome.AlreadySeated;

			if (JoinRefusal(actor, password) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private Result<RoomOutcome>? JoinRefusal(BanchoConnection actor, string password)
	{
		var rights = actor.EffectivePermissions(Context.Clock.GetUtcNow());
		if (Members.Banned.Contains(actor.Record)) return RoomOutcome.Banned;
		if (!rights.Allows(UserPermissions.RoomJoin)) return Result<RoomOutcome>.Denied(UserPermissions.RoomJoin);

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return RoomOutcome.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return RoomOutcome.IsObserver;
		if (WrongPassword(password, rights.Allows(UserPermissions.RoomManageAny))) return RoomOutcome.WrongPassword;

		return null;
	}

	private bool WrongPassword(string password, bool managesAnyRoom)
	{
		return !string.IsNullOrEmpty(Settings.Password) && Settings.Password != password && !managesAnyRoom;
	}

	/// <summary>Removes a seated player on their own request.</summary>
	/// <param name="actor">The player's game connection.</param>
	/// <returns>Ok, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Leave(BanchoConnection actor)
	{
		lock (_sync)
		{
			return IsClosed ? RoomOutcome.RoomClosed : LeaveSeat(actor);
		}
	}

	/// <summary>Removes a seated player on a manager's order.</summary>
	/// <param name="actor">The manager.</param>
	/// <param name="player">The user to remove.</param>
	/// <returns>Ok, IsManager, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Kick(UserRecord actor, UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.IsManagedBy(player)) return RoomOutcome.IsManager;
			if (Slots.Find(player) is not { Player: { } seated }) return RoomOutcome.NotInRoom;

			var slot = Vacate(seated)!;
			var progress = AdvanceRound();
			Emit(new RoomPlayerKicked(this, actor, seated, slot.Index, Authority.Host, progress));
			ReportIfEmpty();
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Bans a user from the room and removes them if seated.</summary>
	/// <param name="actor">The manager.</param>
	/// <param name="player">The user to ban.</param>
	/// <returns>Ok, IsManager or RoomClosed; banning a banned user again returns Ok.</returns>
	internal Result<RoomOutcome> Ban(UserRecord actor, UserRecord player)
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

			Emit(new RoomPlayerBanned(this, actor, player, vacated, evicted, Authority.Host, progress));
			if (evicted is not null) ReportIfEmpty();

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
	/// <param name="actor">The manager inviting.</param>
	/// <param name="target">The invited user.</param>
	/// <returns>Ok, TargetOffline, AlreadyInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> Invite(UserRecord actor, User target)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return RoomOutcome.TargetOffline;
			if (Slots.Find(target.Record) is not null) return RoomOutcome.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, actor, target.Record));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Adds a tournament connection as an observer.</summary>
	/// <param name="actor">The tournament connection observing.</param>
	/// <returns>Ok, IsPlayer or RoomClosed.</returns>
	internal Result<RoomOutcome> ObserverJoin(TourneyConnection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(actor.Record) is not null) return RoomOutcome.IsPlayer;
			if (!Members.MutableObservers.Add(actor)) return RoomOutcome.Ok;

			Emit(new RoomObserverJoined(this, actor));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Removes a tournament connection from the room's observers.</summary>
	/// <param name="actor">The tournament connection leaving.</param>
	/// <returns>Ok, NotObserver or RoomClosed.</returns>
	internal Result<RoomOutcome> ObserverLeave(TourneyConnection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return RoomOutcome.NotObserver;

			Emit(new RoomObserverLeft(this, actor));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Closes the room, evicting its players and ending its round.</summary>
	/// <param name="actor">The user who closes the room, or <see langword="null" /> when the server closes it.</param>
	/// <returns>Ok; a room that has closed already returns Ok and does nothing.</returns>
	internal Result<RoomOutcome> Close(UserRecord? actor)
	{
		lock (_sync)
		{
			if (!IsClosed) Context.Lobby.Close(this, actor);
			return RoomOutcome.Ok;
		}
	}
}