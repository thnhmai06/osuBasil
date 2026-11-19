using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed partial class Room
{
	public RoomOutcome AddReferee(UserRecord user)
	{
		if (IsClosed) return RoomOutcome.RoomClosed;
		if (Authority.Creator.Equals(user)) return RoomOutcome.IsCreator;
		if (Authority.Referees.Contains(user)) return RoomOutcome.AlreadyReferee;
		if (Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomOutcome.TooManyReferees;

		Authority.MutableReferees.Add(user);
		return RoomOutcome.Ok;
	}

	public RoomOutcome RemoveReferee(UserRecord user)
	{
		if (IsClosed) return RoomOutcome.RoomClosed;
		return Authority.MutableReferees.Remove(user) ? RoomOutcome.Ok : RoomOutcome.NotReferee;
	}

	public (RoomOutcome Outcome, bool Changed) SetHost(UserRecord? host)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, false);
		if (host is not null && Slots.Find(host) is null) return (RoomOutcome.NotInRoom, false);
		if (Equals(Authority.Host, host)) return (RoomOutcome.Ok, false);

		Authority.Host = host;
		return (RoomOutcome.Ok, true);
	}

	public (RoomOutcome Outcome, int Slot, RoomRoundProgress? Progress) Kick(UserRecord player, DateTimeOffset now)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, 0, null);
		if (Authority.IsManagedBy(player)) return (RoomOutcome.IsManager, 0, null);
		if (Slots.Find(player) is null) return (RoomOutcome.NotInRoom, 0, null);

		var slot = Vacate(player)!;
		return (RoomOutcome.Ok, slot.Index, AdvanceRound(now));
	}

	public (RoomOutcome Outcome, bool Added, int? Vacated, RoomRoundProgress? Progress) Ban(UserRecord player,
		DateTimeOffset now)
	{
		if (IsClosed) return (RoomOutcome.RoomClosed, false, null, null);
		if (Authority.IsManagedBy(player)) return (RoomOutcome.IsManager, false, null, null);
		if (!_banned.Add(player)) return (RoomOutcome.Ok, false, null, null);

		int? vacated = null;
		RoomRoundProgress? progress = null;
		if (Slots.Find(player) is not null)
		{
			vacated = Vacate(player)?.Index;
			progress = AdvanceRound(now);
		}

		return (RoomOutcome.Ok, true, vacated, progress);
	}

	public RoomOutcome Unban(UserRecord player)
	{
		if (IsClosed) return RoomOutcome.RoomClosed;
		return _banned.Remove(player) ? RoomOutcome.Ok : RoomOutcome.NotBanned;
	}

	public bool IsWrongPassword(string password, bool managesAnyRoom)
	{
		return !string.IsNullOrEmpty(Settings.Password) && Settings.Password != password && !managesAnyRoom;
	}
}