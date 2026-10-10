using System.Collections;
using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Contracts.Multiplayer.Room;

/// <summary>An osu! multiplayer room's 16 player slots, and whether the room's slots are locked as a whole.</summary>
public sealed class RoomSlots : IReadOnlyList<RoomSlot>
{
	public const int MaxSlotCount = 16;

	/// <summary>The room these slots belong to.</summary>
	public readonly Room Room;

	private readonly ImmutableArray<RoomSlot> _slots;

	internal RoomSlots(Room room)
	{
		Room = room;
		_slots = [.. Enumerable.Range(1, MaxSlotCount).Select(index => new RoomSlot(this, index))];
	}

	/// <summary>Gets a value that indicates whether the room's slots are locked as a whole.</summary>
	/// <remarks>A locked room stops players from changing their own slot or team; referees still can.</remarks>
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

	/// <summary>Finds the slot occupied by a player.</summary>
	/// <param name="user">The player to look up.</param>
	/// <returns>The player's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(User user)
	{
		return _slots.FirstOrDefault(s => user.Equals(s.Player?.User));
	}

	/// <summary>Finds the slot occupied by a player connection.</summary>
	/// <param name="player">The connection to look up.</param>
	/// <returns>The connection's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(BanchoConnection player)
	{
		return _slots.FirstOrDefault(s => ReferenceEquals(player, s.Player));
	}

	/// <summary>Gets the slot with a given number, or <see langword="null" /> when the number is outside 1 to 16.</summary>
	public RoomSlot? At(int index)
	{
		return index is >= 1 and <= MaxSlotCount ? _slots[index - 1] : null;
	}

	private static void ThrowIfOutOfRangeIndex(int index)
	{
		if (index is < 1 or > MaxSlotCount)
			throw new ArgumentOutOfRangeException(nameof(index), index,
				$"Slot index must be between 1 and {MaxSlotCount}.");
	}
}