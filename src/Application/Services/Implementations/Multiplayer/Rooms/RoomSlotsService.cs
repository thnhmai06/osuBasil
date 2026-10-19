using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Moves players between slots and changes what a player sets on their own slot.</summary>
internal sealed class RoomSlotsService(
	RoomEventStream events,
	RoomChannelService roomChannel,
	RoomMembershipService membership,
	RoomRoundsService rounds,
	TimeProvider time) : IRoomSlotsService
{
	/// <inheritdoc />
	public Task<RoomResult> ToggleSlotLockAsync(Room room, Connection by, int index,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => ToggleSlotLock(room, by, index), cancellationToken);
	}

	private RoomResult ToggleSlotLock(Room room, Connection by, int index)
	{
		if (!RoomRules.IsHostOrManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Slots.At(index) is not { } slot) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(slot.Player, by)) return RoomResult.OwnSlot;

		var locking = !slot.Locked;

		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			RoomSlotsMechanics.Vacate(room, player);
			progress = rounds.AdvanceRound(room);
		}

		slot.Locked = locking;
		events.Emit(new RoomSlotLockChanged(room, slot.Index, locking, evicted, room.Authority.Host, progress));

		if (evicted is not null)
		{
			roomChannel.LeaveChannel(room, evicted);
			membership.ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}
}