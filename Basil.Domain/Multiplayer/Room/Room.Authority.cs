using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed partial class Room
{
	public RoomActionResult AddReferee(UserRecord user)
	{
		if (IsClosed) return RoomActionResult.RoomClosed;
		if (Authority.Creator.Equals(user)) return RoomActionResult.IsCreator;
		if (Authority.Referees.Contains(user)) return RoomActionResult.AlreadyReferee;
		if (Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomActionResult.TooManyReferees;

		Authority.MutableReferees.Add(user);
		return RoomActionResult.Ok;
	}

	public RoomActionResult RemoveReferee(UserRecord user)
	{
		if (IsClosed) return RoomActionResult.RoomClosed;
		return Authority.MutableReferees.Remove(user) ? RoomActionResult.Ok : RoomActionResult.NotReferee;
	}

	public (RoomActionResult Outcome, bool Changed) SetHost(UserRecord? host)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, false);
		if (host is not null && Slots.Find(host) is null) return (RoomActionResult.NotInRoom, false);
		if (Equals(Authority.Host, host)) return (RoomActionResult.Ok, false);

		Authority.Host = host;
		return (RoomActionResult.Ok, true);
	}

	public (RoomActionResult Outcome, int Slot, RoomRoundProgress? Progress) Kick(UserRecord player, DateTimeOffset now)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, 0, null);
		if (Authority.IsManagedBy(player)) return (RoomActionResult.IsManager, 0, null);
		if (Slots.Find(player) is null) return (RoomActionResult.NotInRoom, 0, null);

		var slot = Vacate(player)!;
		return (RoomActionResult.Ok, slot.Index, AdvanceRound(now));
	}

	public (RoomActionResult Outcome, bool Added, int? Vacated, RoomRoundProgress? Progress) Ban(UserRecord player,
		DateTimeOffset now)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, false, null, null);
		if (Authority.IsManagedBy(player)) return (RoomActionResult.IsManager, false, null, null);
		if (!_banned.Add(player)) return (RoomActionResult.Ok, false, null, null);

		int? vacated = null;
		RoomRoundProgress? progress = null;
		if (Slots.Find(player) is not null)
		{
			vacated = Vacate(player)?.Index;
			progress = AdvanceRound(now);
		}

		return (RoomActionResult.Ok, true, vacated, progress);
	}

	public RoomActionResult Unban(UserRecord player)
	{
		if (IsClosed) return RoomActionResult.RoomClosed;
		return _banned.Remove(player) ? RoomActionResult.Ok : RoomActionResult.NotBanned;
	}

	public bool IsWrongPassword(string password, bool managesAnyRoom)
	{
		return !string.IsNullOrEmpty(Settings.Password) && Settings.Password != password && !managesAnyRoom;
	}
}