using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal bool Involves(UserRecord user)
	{
		return Authority.IsManagedBy(user) || Slots.Find(user) is not null;
	}

	internal RoomOutcome JoinClaiming(StableConnection actor, string password, bool managesAnyRoom)
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

	internal RoomOutcome Join(StableConnection actor, string password, bool managesAnyRoom)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			var stale = Slots.Find(actor.Record)?.Player;
			if (stale is not null && (ReferenceEquals(stale, actor) || stale.IsOpen)) return RoomOutcome.AlreadySeated;

			if (JoinRefusal(actor, password, managesAnyRoom) is { } refusal) return refusal;

			return TakeSeat(actor, stale);
		}
	}

	private RoomOutcome? JoinRefusal(StableConnection actor, string password, bool managesAnyRoom)
	{
		if (Members.Banned.Contains(actor.Record)) return RoomOutcome.Banned;

		if (RoomOf(actor) is { } other && !ReferenceEquals(other, this)) return RoomOutcome.InAnotherRoom;
		if (Members.Observers.Any(observer => observer.Record.Equals(actor.Record))) return RoomOutcome.IsObserver;
		if (WrongPassword(password, managesAnyRoom)) return RoomOutcome.WrongPassword;

		return null;
	}

	private bool WrongPassword(string password, bool managesAnyRoom)
	{
		return !string.IsNullOrEmpty(Settings.Password) && Settings.Password != password && !managesAnyRoom;
	}

	internal RoomOutcome Leave(StableConnection actor)
	{
		lock (_sync)
		{
			return IsClosed ? RoomOutcome.RoomClosed : LeaveSeat(actor);
		}
	}

	internal RoomOutcome Kick(UserRecord actor, UserRecord player)
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

	internal RoomOutcome Ban(UserRecord actor, UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.IsManagedBy(player)) return RoomOutcome.IsManager;
			if (!Members.MutableBanned.Add(player)) return RoomOutcome.Ok;

			int? vacated = null;
			StableConnection? evicted = null;
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

	internal RoomOutcome Unban(UserRecord player)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableBanned.Remove(player)) return RoomOutcome.NotBanned;

			Emit(new RoomPlayerUnbanned(this, player));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome Invite(UserRecord actor, User target)
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

	internal RoomOutcome ObserverJoin(TourneyConnection actor)
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

	internal RoomOutcome ObserverLeave(TourneyConnection actor)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Members.MutableObservers.Remove(actor)) return RoomOutcome.NotObserver;

			Emit(new RoomObserverLeft(this, actor));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome Close(UserRecord? actor)
	{
		lock (_sync)
		{
			if (!IsClosed) Context.Lobby.Close(this, actor);
			return RoomOutcome.Ok;
		}
	}
}