using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomActionResult SetLocked(bool locked)
	{
		using (State.Enter())
		{
			var (outcome, changed) = State.SetLocked(locked);
			if (changed) Emit(new RoomLockChanged(this, locked));
			return outcome;
		}
	}

	internal RoomActionResult Configure(Session actor, RoomSettingsChange change)
	{
		using (State.Enter())
		{
			var (outcome, changed) = State.Configure(PlayerOf(actor), change);
			if (changed)
				Emit(new RoomSettingsChanged(this, change with { Password = null },
					change.Password is not null));
			return outcome;
		}
	}
}