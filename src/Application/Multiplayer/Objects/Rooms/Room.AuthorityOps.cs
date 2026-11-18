using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome AddReferee(UserRecord actor, UserRecord user)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Authority.Creator.Equals(user)) return RoomOutcome.IsCreator;
			if (Authority.Referees.Contains(user)) return RoomOutcome.AlreadyReferee;
			if (Authority.Referees.Count >= RoomAuthority.MaxReferees) return RoomOutcome.TooManyReferees;

			Authority.MutableReferees.Add(user);
			Emit(new RoomRefereeAdded(this, actor, user));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome RemoveReferee(UserRecord actor, UserRecord user)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Authority.MutableReferees.Remove(user)) return RoomOutcome.NotReferee;

			Emit(new RoomRefereeRemoved(this, actor, user));
			return RoomOutcome.Ok;
		}
	}

	internal RoomOutcome SetHost(UserRecord actor, StableConnection? host)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (host is not null && Slots.Find(host) is null) return RoomOutcome.NotInRoom;
			if (ReferenceEquals(Authority.Host, host)) return RoomOutcome.Ok;

			Authority.Host = host;
			Emit(new RoomHostChanged(this, actor, host));
			return RoomOutcome.Ok;
		}
	}
}