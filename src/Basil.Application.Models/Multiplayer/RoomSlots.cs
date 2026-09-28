using System.Collections;
using System.Collections.Immutable;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Models.Multiplayer;

/// <summary>
///     Owns an osu! multiplayer room's 16 player slots and the rules that span the whole set:
///     finding a slot to join, locating a player's slot, resizing the room, moving a player between
///     slots, and locking or unlocking a slot.
/// </summary>
public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	public const int MaxSlotCount = 16;
	private readonly ImmutableArray<RoomSlot> _slots;

	public readonly Room Room;

	public bool Locked { get; set; } // can not move slot

	internal RoomSlots(Room room)
	{
		Room = room;
		_slots = [.. Enumerable.Range(0, MaxSlotCount).Select(index => new RoomSlot(this, index))];
	}

	/// <summary>Finds the slot occupied by a player.</summary>
	/// <param name="who">The player to look up.</param>
	/// <returns>The player's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(User who)
	{
		return _slots.FirstOrDefault(s => who.Equals(s.User));
	}

	public void Resize(int size)
	{
		var playerCount = _slots.Count(s => s.User is not null);
		size = Math.Clamp(size, 1, playerCount);
		var locked = 0;

		foreach (var slot in _slots.Where(s => s.User is null))
			if (locked < size) // should be locked
			{
				slot.Locked = true;
				++locked;
			}
			else
			{
				slot.Locked = false;
			}
	}

	public RoomSlot? Join(User user)
	{
		if (Room.Banned.Contains(user))
			throw new InvalidOperationException("The user is banned from this room.");

		var alreadySlot = _slots.FirstOrDefault(s => s is { Locked: false, User: not null });
		if (alreadySlot is not null) return alreadySlot;

		var slot = Find(user);
		if (slot is not null)
		{
			slot.User = user;
			if (Room.Settings.TeamType.NeedSplitTeam())
			{
				var redCount = _slots.Count(s => s.Team == GameTeam.Red);
				var blueCount = _slots.Count(s => s.Team == GameTeam.Blue);

				slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
			}
		}

		return slot;
	}

	public void Leave(RoomSlot slot)
	{
		slot.ThrowIfDifferentRoom(this);
		slot.User = null;
	}

	public void Leave(User user)
	{
		var slot = Find(user);
		if (slot is not null) Leave(slot);
	}

	public static void Move(RoomSlot from, RoomSlot to)
	{
		from.MoveTo(to);
	}

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

	private static void ThrowIfOutOfRangeIndex(int index)
	{
		if (index is < 1 or > MaxSlotCount)
			throw new ArgumentOutOfRangeException(nameof(index), index,
				$"Slot index must be between 1 and {MaxSlotCount}.");
	}
}