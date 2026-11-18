using System.Collections;
using System.Collections.Immutable;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	public readonly Room Room;

	private readonly ImmutableArray<RoomSlot> _slots;

	internal RoomSlots(Room room)
	{
		Room = room;
		_slots = [.. Enumerable.Range(1, RoomRules.MaxPlayers).Select(index => new RoomSlot(this, index))];
	}

	public bool Locked { get; internal set; }

	public int Count => _slots.Length;

	public RoomSlot this[int index]
	{
		get
		{
			ThrowIfOutOfRangeIndex(index);
			return _slots[index - 1];
		}
	}

	public IEnumerator<RoomSlot> GetEnumerator()
	{
		return ((IEnumerable<RoomSlot>)_slots).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public RoomSlot? Find(UserRecord user)
	{
		return _slots.FirstOrDefault(s => user.Equals(s.Player?.Record));
	}

	public RoomSlot? Find(StableSession player)
	{
		return _slots.FirstOrDefault(s => ReferenceEquals(player, s.Player));
	}

	internal void ResetPlayers()
	{
		foreach (var slot in this.Where(s => s.Status is RoomSlotStatus.Playing or RoomSlotStatus.Complete))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	internal void Resize(int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, RoomRules.MaxPlayers);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - this.Count(s => s.Player is not null);
		foreach (var slot in this.Where(s => s.Player is null))
			slot.Locked = open-- <= 0;
	}

	public RoomSlot? At(int index)
	{
		return index is >= 1 and <= RoomRules.MaxPlayers ? _slots[index - 1] : null;
	}

	private static void ThrowIfOutOfRangeIndex(int index)
	{
		if (index is < 1 or > RoomRules.MaxPlayers)
			throw new ArgumentOutOfRangeException(nameof(index), index,
				$"Slot index must be between 1 and {RoomRules.MaxPlayers}.");
	}
}