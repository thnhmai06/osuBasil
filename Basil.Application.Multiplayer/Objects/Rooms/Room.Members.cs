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
		return IsManagedBy(user) || Slots.Find(user) is not null;
	}

	internal LobbyResult JoinClaiming(StableSession actor, string password, bool managesAnyRoom)
	{
		var lobby = Context.Lobby;
		if (!lobby.TryClaim(actor, this)) return LobbyResult.InAnotherRoom;

		try
		{
			return Join(actor, password, managesAnyRoom);
		}
		finally
		{
			lobby.Release(actor, this);
		}
	}

	internal LobbyResult Join(StableSession actor, string password, bool managesAnyRoom)
	{
		using (Enter())
		{
			if (IsClosed) return LobbyResult.RoomClosed;

			var stale = SessionOf(actor.Record);
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen)) return LobbyResult.AlreadySeated;

			if (JoinRefusal(actor, password, managesAnyRoom) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private LobbyResult? JoinRefusal(StableSession actor, string password, bool managesAnyRoom)
	{
		if (Members.Banned.Contains(actor.Record)) return LobbyResult.Banned;

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return LobbyResult.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return LobbyResult.IsObserver;
		if (!managesAnyRoom && !State.Settings.PasswordMatches(password)) return LobbyResult.WrongPassword;

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

	internal LobbyResult Invite(UserRecord actor, User target)
	{
		using (Enter())
		{
			if (IsClosed) return LobbyResult.RoomClosed;
			if (!target.Connections.Any(connection => connection.IsOpen)) return LobbyResult.TargetOffline;
			if (Slots.Find(target.Record) is not null) return LobbyResult.AlreadyInRoom;

			Emit(new RoomPlayerInvited(this, actor, target.Record));
			return LobbyResult.Ok;
		}
	}

	internal LobbyResult ObserverJoin(TourneySession actor)
	{
		using (Enter())
		{
			if (IsClosed) return LobbyResult.RoomClosed;
			if (Slots.Find(actor.Record) is not null) return LobbyResult.IsPlayer;
			if (!Members.MutableObservers.Add(actor)) return LobbyResult.Ok;

			State.Members.Add(State.Members.Of(actor.Record));
			Emit(new RoomObserverJoined(this, actor));
			return LobbyResult.Ok;
		}
	}

	internal LobbyResult ObserverLeave(TourneySession actor)
	{
		using (Enter())
		{
			if (IsClosed) return LobbyResult.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return LobbyResult.NotObserver;

			if (!Members.Observers.Any(observer => observer.Record.Equals(actor.Record)))
				State.Members.Remove(State.Members.Of(actor.Record));
			Emit(new RoomObserverLeft(this, actor));
			return LobbyResult.Ok;
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