using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal bool Involves(UserRecord user)
	{
		return Authority.IsManagedBy(user) || Slots.Find(user) is not null;
	}

	internal RoomOutcome JoinClaiming(StableSession actor, string password, bool managesAnyRoom)
	{
		var lobby = Context.Lobby;
		if (!lobby.TryClaim(actor, this)) return RoomOutcome.InAnotherRoom;

		try
		{
			return Join(actor, password, managesAnyRoom);
		}
		finally
		{
			lobby.Release(actor, this);
		}
	}

	internal RoomOutcome Join(StableSession actor, string password, bool managesAnyRoom)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			var stale = SessionOf(actor.Record);
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen)) return RoomOutcome.AlreadySeated;

			if (JoinRefusal(actor, password, managesAnyRoom) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private RoomOutcome? JoinRefusal(StableSession actor, string password, bool managesAnyRoom)
	{
		if (Members.Banned.Contains(actor.Record)) return RoomOutcome.Banned;

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return RoomOutcome.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return RoomOutcome.IsObserver;
		if (State.IsWrongPassword(password, managesAnyRoom)) return RoomOutcome.WrongPassword;

		return null;
	}

	internal RoomOutcome Leave(StableSession actor)
	{
		using (State.Enter())
		{
			return IsClosed ? RoomOutcome.RoomClosed : LeaveSeat(actor);
		}
	}

	internal RoomOutcome Kick(UserRecord actor, UserRecord player)
	{
		using (State.Enter())
		{
			var seated = SessionOf(player);
			var (outcome, slot, progress) = State.Kick(player, Now);
			if (outcome is not RoomOutcome.Ok) return outcome;

			Unseat(player);
			Emit(new RoomPlayerKicked(this, actor, seated!, slot, Host, progress));
			ReportIfEmpty();
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome Ban(UserRecord actor, UserRecord player)
	{
		using (State.Enter())
		{
			var (outcome, added, vacated, progress) = State.Ban(player, Now);
			if (outcome is not RoomOutcome.Ok || !added) return outcome;

			var evicted = vacated is null ? null : Unseat(player);
			Emit(new RoomPlayerBanned(this, actor, player, vacated, evicted, Host, progress));
			if (evicted is not null) ReportIfEmpty();

			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome Unban(UserRecord player)
	{
		using (State.Enter())
		{
			var outcome = State.Unban(player);
			if (outcome is RoomOutcome.Ok) Emit(new RoomPlayerUnbanned(this, player));
			return outcome;
		}
	}

	internal RoomOutcome Invite(UserRecord actor, User target)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return RoomOutcome.TargetOffline;
			if (Slots.Find(target.Record) is not null) return RoomOutcome.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, actor, target.Record));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome ObserverJoin(TourneySession actor)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(actor.Record) is not null) return RoomOutcome.IsPlayer;
			if (!Members.MutableObservers.Add(actor)) return RoomOutcome.Ok;

			Emit(new RoomObserverJoined(this, actor));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome ObserverLeave(TourneySession actor)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return RoomOutcome.NotObserver;

			Emit(new RoomObserverLeft(this, actor));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome Close(UserRecord? actor)
	{
		using (State.Enter())
		{
			if (!IsClosed) Context.Lobby.Close(this, actor);
			return RoomOutcome.Ok;
		}
	}
}