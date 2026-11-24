using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomJoinResult JoinClaiming(StableSession actor, string password, bool managesAnyRoom)
	{
		var lobby = Context.Lobby;
		if (!lobby.TryClaim(actor, this)) return RoomJoinResult.InAnotherRoom;

		try
		{
			return Join(actor, password, managesAnyRoom);
		}
		finally
		{
			lobby.Release(actor, this);
		}
	}

	internal RoomJoinResult Join(StableSession actor, string password, bool managesAnyRoom)
	{
		using (Enter())
		{
			if (IsClosed) return RoomJoinResult.RoomClosed;

			var stale = SessionOf(actor.Record);
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen))
				return RoomJoinResult.AlreadySeated;

			if (JoinRefusal(actor, password, managesAnyRoom) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private RoomJoinResult? JoinRefusal(StableSession actor, string password, bool managesAnyRoom)
	{
		if (Members.Banned.Contains(actor.Record)) return RoomJoinResult.Banned;

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return RoomJoinResult.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return RoomJoinResult.IsObserver;
		if (!managesAnyRoom && !State.Settings.PasswordMatches(password)) return RoomJoinResult.WrongPassword;

		return null;
	}

	internal RoomActionResult Leave(StableSession actor)
	{
		using (Enter())
		{
			return IsClosed ? RoomActionResult.RoomClosed : LeaveSeat(actor);
		}
	}

	internal RoomActionResult Kick(UserRecord actor, UserRecord player)
	{
		using (Enter())
		{
			var seated = SessionOf(player);
			var (outcome, slot, progress) = State.Members.Kick(State.Members.Of(player), Now);
			if (outcome is not RoomActionResult.Ok) return outcome;

			Unseat(player);
			Emit(new RoomPlayerKicked(this, actor, seated!, slot, Host, progress));
			if (progress is { Completed: true }) SettleRound();
			ReportIfEmpty();
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult Ban(UserRecord actor, UserRecord player)
	{
		using (Enter())
		{
			var (outcome, added, vacated, progress) = State.Members.Ban(State.Members.Of(player), Now);
			if (outcome is not RoomActionResult.Ok || !added) return outcome;

			var evicted = vacated is null ? null : Unseat(player);
			Emit(new RoomPlayerBanned(this, actor, player, vacated, evicted, Host, progress));
			if (progress is { Completed: true }) SettleRound();
			if (evicted is not null) ReportIfEmpty();

			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult Unban(UserRecord player)
	{
		using (Enter())
		{
			var outcome = State.Members.Unban(State.Members.Of(player));
			if (outcome is RoomActionResult.Ok) Emit(new RoomPlayerUnbanned(this, player));
			return outcome;
		}
	}

	internal RoomInviteResult Invite(UserRecord actor, UserSessions target)
	{
		using (Enter())
		{
			if (IsClosed) return RoomInviteResult.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return RoomInviteResult.TargetOffline;
			if (Slots.Find(target.Record) is not null) return RoomInviteResult.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, actor, target.Record));
			return RoomInviteResult.Ok;
		}
	}

	internal RoomJoinResult ObserverJoin(TourneySession actor)
	{
		using (Enter())
		{
			if (IsClosed) return RoomJoinResult.RoomClosed;
			if (Slots.Find(actor.Record) is not null) return RoomJoinResult.IsPlayer;
			if (!Members.MutableObservers.Add(actor)) return RoomJoinResult.Ok;

			State.Members.Add(actor.Record);
			Emit(new RoomObserverJoined(this, actor));
			return RoomJoinResult.Ok;
		}
	}

	internal RoomJoinResult ObserverLeave(TourneySession actor)
	{
		using (Enter())
		{
			if (IsClosed) return RoomJoinResult.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return RoomJoinResult.NotObserver;

			if (!Members.Observers.Any(observer => observer.Record.Equals(actor.Record)))
				State.Members.Remove(actor.Record);
			Emit(new RoomObserverLeft(this, actor));
			return RoomJoinResult.Ok;
		}
	}

	internal RoomActionResult Close(UserRecord? actor)
	{
		using (Enter())
		{
			if (!IsClosed) Context.Lobby.Close(this, actor);
			return RoomActionResult.Ok;
		}
	}
}