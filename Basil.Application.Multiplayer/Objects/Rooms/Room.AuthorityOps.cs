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

	internal RoomActionResult SetHost(UserRecord actor, UserRecord? host)
	{
		using (Enter())
		{
			var session = host is null ? null : SessionOf(host);
			if (host is not null && session is null)
				return IsClosed ? RoomActionResult.RoomClosed : RoomActionResult.NotInRoom;

			var (outcome, changed) = State.Members.SetHost(host is null ? null : State.Members.Of(host));
			if (changed) Emit(new RoomHostChanged(this, actor, session));
			return outcome;
		}
	}
}