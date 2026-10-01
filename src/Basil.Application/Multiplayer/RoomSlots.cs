using System.Collections;
using System.Collections.Immutable;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer;

/// <summary>
///     Owns an osu! multiplayer room's 16 player slots and the rules that span the whole set:
///     finding a slot to join, locating a player's slot, resizing the room, moving a player between
///     slots, and locking or unlocking a slot.
/// </summary>
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

	/// <summary>Resizes the room, leaving occupied slots untouched.</summary>
	/// <param name="size">The new number of available slots, from 1 to 16.</param>
	internal void Resize(int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSlotCount);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - _slots.Count(s => s.Player is not null);
		foreach (var slot in _slots.Where(s => s.Player is null))
			slot.SetLocked(open-- <= 0);
	}

	/// <summary>Seats a player in the lowest-index empty, unlocked slot.</summary>
	/// <param name="player">The connection to seat.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when no slot is available.</returns>
	/// <exception cref="InvalidOperationException">
	///     The user is banned or the room is full.
	/// </exception>
	internal RoomSlot? Seat(BanchoConnection player)
	{
		if (Room.Banned.Contains(player.User))
			throw new InvalidOperationException("The user is banned from this room.");

		if (Find(player) is { } existing) return existing;

		var slot = _slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		slot.Occupy(player);

		if (Room.TeamType.NeedSplitTeam())
		{
			var redCount = _slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = _slots.Count(s => s.Team == GameTeam.Blue);
			slot.SetTeam(redCount <= blueCount ? GameTeam.Red : GameTeam.Blue);
		}

		Room.Emit(new RoomPlayerJoined(Room, player, slot.Index));
		return slot;
	}

	/// <summary>Clears the slot occupied by <paramref name="player" />, if any.</summary>
	/// <param name="player">The connection to remove.</param>
	/// <returns>The slot that was vacated, or <see langword="null" /> when the connection was not seated.</returns>
	internal RoomSlot? Vacate(BanchoConnection player)
	{
		var slot = Find(player);
		if (slot is null) return null;

		Room.PassHostFrom(player);

		slot.Clear();
		return slot;
	}

	private static void ThrowIfOutOfRangeIndex(int index)
	{
		if (index is < 1 or > MaxSlotCount)
			throw new ArgumentOutOfRangeException(nameof(index), index,
				$"Slot index must be between 1 and {MaxSlotCount}.");
	}
}