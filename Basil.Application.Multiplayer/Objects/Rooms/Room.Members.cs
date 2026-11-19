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

	internal RoomActionResult JoinClaiming(StableSession actor, string password, bool managesAnyRoom)
	{
		var lobby = Context.Lobby;
		if (!lobby.TryClaim(actor, this)) return RoomActionResult.InAnotherRoom;

		try
		{
			return Join(actor, password, managesAnyRoom);
		}
		finally
		{
			lobby.Release(actor, this);
		}
	}

	internal RoomActionResult Join(StableSession actor, string password, bool managesAnyRoom)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomActionResult.RoomClosed;

			var stale = SessionOf(actor.Record);
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen)) return RoomActionResult.AlreadySeated;

			if (JoinRefusal(actor, password, managesAnyRoom) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private RoomActionResult? JoinRefusal(StableSession actor, string password, bool managesAnyRoom)
	{
		if (Members.Banned.Contains(actor.Record)) return RoomActionResult.Banned;

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return RoomActionResult.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return RoomActionResult.IsObserver;
		if (State.IsWrongPassword(password, managesAnyRoom)) return RoomActionResult.WrongPassword;

		return null;
	}

	internal RoomActionResult Leave(StableSession actor)
	{
		using (State.Enter())
		{
			return IsClosed ? RoomActionResult.RoomClosed : LeaveSeat(actor);
		}
	}

	internal RoomActionResult Kick(UserRecord actor, UserRecord player)
	{
		using (State.Enter())
		{
			var seated = SessionOf(player);
			var (outcome, slot, progress) = State.Kick(player, Now);
			if (outcome is not RoomActionResult.Ok) return outcome;

			Unseat(player);
			Emit(new RoomPlayerKicked(this, actor, seated!, slot, Host, progress));
			ReportIfEmpty();
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult Ban(UserRecord actor, UserRecord player)
	{
		using (State.Enter())
		{
			var (outcome, added, vacated, progress) = State.Ban(player, Now);
			if (outcome is not RoomActionResult.Ok || !added) return outcome;

			var evicted = vacated is null ? null : Unseat(player);
			Emit(new RoomPlayerBanned(this, actor, player, vacated, evicted, Host, progress));
			if (evicted is not null) ReportIfEmpty();

			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult Unban(UserRecord player)
	{
		using (State.Enter())
		{
			var outcome = State.Unban(player);
			if (outcome is RoomActionResult.Ok) Emit(new RoomPlayerUnbanned(this, player));
			return outcome;
		}
	}

	internal RoomActionResult Invite(UserRecord actor, User target)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomActionResult.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return RoomActionResult.TargetOffline;
			if (Slots.Find(target.Record) is not null) return RoomActionResult.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, actor, target.Record));
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult ObserverJoin(TourneySession actor)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomActionResult.RoomClosed;
			if (Slots.Find(actor.Record) is not null) return RoomActionResult.IsPlayer;
			if (!Members.MutableObservers.Add(actor)) return RoomActionResult.Ok;

			Emit(new RoomObserverJoined(this, actor));
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult ObserverLeave(TourneySession actor)
	{
		using (State.Enter())
		{
			if (IsClosed) return RoomActionResult.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return RoomActionResult.NotObserver;

			Emit(new RoomObserverLeft(this, actor));
			return RoomActionResult.Ok;
		}
	}

	internal RoomActionResult Close(UserRecord? actor)
	{
		using (State.Enter())
		{
			if (!IsClosed) Context.Lobby.Close(this, actor);
			return RoomActionResult.Ok;
		}
	}
}