using System.Collections;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Owns an osu! multiplayer room's 16 player slots and the rules that span the whole set:
///     finding a slot to join, locating a player's slot, resizing the room, moving a player between
///     slots, and locking or unlocking a slot.
/// </summary>
public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	private const int SlotCount = 16;

	private readonly Room _room;
	private readonly RoomSlot[] _slots;

	internal RoomSlots(Room room)
	{
		_room = room;
		_slots = new RoomSlot[SlotCount];
		for (var i = 0; i < SlotCount; i++)
			_slots[i] = new RoomSlot(this, i);
	}

	/// <summary>Gets the slot at the given zero-based index.</summary>
	/// <param name="index">The zero-based slot index.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	public RoomSlot this[int index] =>
		index >= 0 && index < SlotCount
			? _slots[index]
			: throw new ArgumentOutOfRangeException(nameof(index), index, "Slot index must be between 0 and 15.");

	/// <summary>Gets the number of slots, always 16.</summary>
	public int Count => _slots.Length;

	/// <summary>Gets a value indicating whether every slot is occupied.</summary>
	public bool IsFull => this.All(s => s.Availability != RoomSlotAvailability.Open);

	/// <summary>Gets a value indicating whether every playing player has finished loading the beatmap.</summary>
	public bool AllLoaded => this.All(s => s.Status != RoomSlotStatus.Playing || s.BeatmapLoaded);

	/// <summary>Gets a value indicating whether every playing player has skipped the beatmap's intro.</summary>
	public bool AllSkipped => this.All(s => s.Status != RoomSlotStatus.Playing || s.IntroSkipped);

	/// <summary>Gets a value indicating whether any player is currently playing.</summary>
	public bool AnyPlaying => this.Any(s => s.Status == RoomSlotStatus.Playing);

	/// <summary>Finds the first open slot.</summary>
	/// <returns>The first open slot, or <see langword="null" /> when no slot is open.</returns>
	public RoomSlot? FindOpen()
	{
		return this.FirstOrDefault(s => s.Availability == RoomSlotAvailability.Open);
	}

	/// <summary>Finds the slot occupied by a player.</summary>
	/// <param name="player">The player to look up.</param>
	/// <returns>The player's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(User player)
	{
		return this.FirstOrDefault(s => player.Equals(s.User));
	}

	/// <summary>Gets the zero-based index of a slot.</summary>
	/// <param name="slot">The slot to locate.</param>
	/// <returns>The slot's zero-based index, or -1 when the slot does not belong to this set.</returns>
	public int IndexOf(RoomSlot slot)
	{
		return Array.IndexOf(_slots, slot);
	}

	/// <summary>
	///     Resizes the room, locking slots beyond the new size and unlocking slots within it.
	/// </summary>
	/// <remarks>Occupied slots beyond the new size are left occupied.</remarks>
	/// <param name="size">The number of slots the room should have available, from 1 to 16.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is not between 1 and 16.</exception>
	public void Resize(int size)
	{
		if (size is < 1 or > SlotCount)
			throw new ArgumentOutOfRangeException(nameof(size), size, "Room size must be between 1 and 16.");

		for (var i = 0; i < SlotCount; i++)
		{
			var slot = _slots[i];
			if (i < size)
			{
				if (slot.Availability == RoomSlotAvailability.Locked)
					Unlock(i);
			}
			else if (slot.Availability == RoomSlotAvailability.Open)
			{
				Lock(i);
			}
		}
	}

	/// <summary>
	///     Moves a player's complete slot state to another slot.
	/// </summary>
	/// <param name="player">The player to move.</param>
	/// <param name="targetIndex">The zero-based index of the slot to move the player to.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="targetIndex" /> is not a valid slot index.</exception>
	/// <exception cref="InvalidOperationException">
	///     <paramref name="player" /> has no slot in this room, or the target slot is not open.
	/// </exception>
	public void Move(User player, int targetIndex)
	{
		var target = this[targetIndex];
		var slot = Find(player) ??
		           throw new InvalidOperationException("The player does not have a slot in this room.");
		var sourceIndex = IndexOf(slot);

		slot.MoveTo(target);

		_room.Record(new SlotChanged(_room, sourceIndex));
		_room.Record(new SlotChanged(_room, targetIndex));
	}

	/// <summary>
	///     Locks a slot, evicting its occupant if any.
	/// </summary>
	/// <param name="index">The zero-based index of the slot to lock.</param>
	/// <returns>The player evicted from the slot, or <see langword="null" /> when it was not occupied.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	public User? Lock(int index)
	{
		var slot = this[index];
		var occupant = slot.User;

		slot.Lock();
		_room.Record(new SlotLocked(_room, index, occupant));
		return occupant;
	}

	/// <summary>
	///     Unlocks a slot, allowing players to join it again.
	/// </summary>
	/// <param name="index">The zero-based index of the slot to unlock.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is not a valid slot index.</exception>
	public void Unlock(int index)
	{
		this[index].Unlock();
		_room.Record(new SlotChanged(_room, index));
	}

	/// <summary>Records that a slot's team, mods, or occupied-status changed, on behalf of one of this set's slots.</summary>
	/// <param name="index">The zero-based index of the slot that changed.</param>
	internal void RecordSlotChanged(int index)
	{
		_room.Record(new SlotChanged(_room, index));
	}

	/// <summary>Returns an enumerator over the room's slots, in slot-index order.</summary>
	public IEnumerator<RoomSlot> GetEnumerator()
	{
		return ((IEnumerable<RoomSlot>)_slots).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}