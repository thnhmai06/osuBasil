using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomActionResult AddReferee(UserRecord actor, UserRecord user)
	{
		using (Enter())
		{
			var outcome = State.Members.AddReferee(State.Members.Of(user));
			if (outcome is RoomActionResult.Ok) Emit(new RoomRefereeAdded(this, actor, user));
			return outcome;
		}
	}

	internal RoomActionResult RemoveReferee(UserRecord actor, UserRecord user)
	{
		using (Enter())
		{
			var outcome = State.Members.RemoveReferee(State.Members.Of(user));
			if (outcome is RoomActionResult.Ok) Emit(new RoomRefereeRemoved(this, actor, user));
			return outcome;
		}
	}

	internal RoomActionResult SetHost(UserRecord actor, StableSession? host)
	{
		using (Enter())
		{
			if (host is not null && Seated(host) is null)
				return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotInRoom;

			var record = host?.Record;

			var (outcome, changed) = State.Members.SetHost(record is null ? null : State.Members.Of(record));
			if (changed) Emit(new RoomHostChanged(this, actor, host));
			return outcome;
		}
	}
}