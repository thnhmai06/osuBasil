using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Slots and what each player sets on their own slot.</summary>
public interface IRoomSlotsService
{
	/// <summary>Locks or unlocks a slot; locking an occupied slot removes its player.</summary>
	/// <param name="room">The room.</param>
	/// <param name="by">The caller's connection.</param>
	/// <param name="index">The slot number, from 1 to 16.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, SlotNotOpen, OwnSlot or RoomClosed when the room has closed.</returns>
	/// <remarks>A player cannot lock the slot they occupy.</remarks>
	Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index,
		CancellationToken cancellationToken = default);
}
