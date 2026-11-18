using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome AddReferee(UserRecord actor, UserRecord user)
	{
		using (State.Enter())
		{
			var outcome = State.AddReferee(user);
			if (outcome is RoomOutcome.Ok) Emit(new RoomRefereeAdded(this, actor, user));
			return outcome;
		}
	}

	internal RoomOutcome RemoveReferee(UserRecord actor, UserRecord user)
	{
		using (State.Enter())
		{
			var outcome = State.RemoveReferee(user);
			if (outcome is RoomOutcome.Ok) Emit(new RoomRefereeRemoved(this, actor, user));
			return outcome;
		}
	}

	internal RoomOutcome SetHost(UserRecord actor, StableSession? host)
	{
		using (State.Enter())
		{
			if (host is not null && Seated(host) is null)
				return IsClosed ? RoomOutcome.RoomClosed : RoomOutcome.NotInRoom;

			var record = host?.Record;

			var (outcome, changed) = State.SetHost(record);
			if (changed) Emit(new RoomHostChanged(this, actor, host));
			return outcome;
		}
	}
}
