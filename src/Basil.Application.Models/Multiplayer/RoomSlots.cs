using System.Collections;
using System.Collections.Immutable;
using Basil.Application.Models.Sessions;
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

	/// <summary>The room these slots belong to.</summary>
	public readonly Room Room;

	/// <summary>Gets or sets a value that indicates whether the room's slots are locked as a whole.</summary>
	/// <remarks>A locked room stops players from changing their own slot or team; referees still can.</remarks>
	public bool Locked
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			Room.Emit(new RoomLockChanged(Room, value));
		}
	}

	internal RoomSlots(Room room)
	{
		Room = room;
		_slots = [.. Enumerable.Range(1, MaxSlotCount).Select(index => new RoomSlot(this, index))];
	}

	/// <summary>Finds the slot occupied by a player.</summary>
	/// <param name="user">The player to look up.</param>
	/// <returns>The player's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(User user)
	{
		return _slots.FirstOrDefault(s => user.Equals(s.Session?.User));
	}

	/// <summary>Finds the slot occupied by a session.</summary>
	/// <param name="session">The session to look up.</param>
	/// <returns>The session's slot, or <see langword="null" /> when they have none.</returns>
	public RoomSlot? Find(GameSession session)
	{
		return _slots.FirstOrDefault(s => session.Equals(s.Session));
	}

	/// <summary>Resizes the room, leaving occupied slots untouched.</summary>
	/// <param name="size">The new number of available slots, from 1 to 16.</param>
	public void Resize(int size)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSlotCount);

		// Players may sit in any slot, so the size counts usable slots rather than naming a slot
		// range: keep (size - players) empty slots open and lock the remaining empty ones.
		var open = size - _slots.Count(s => s.Session is not null);
		foreach (var slot in _slots.Where(s => s.Session is null))
			slot.SetLocked(open-- <= 0);

		Room.Emit(new RoomResized(Room, size));
	}

	/// <summary>Seats a session in the lowest-index empty, unlocked slot.</summary>
	/// <param name="session">The session to seat.</param>
	/// <returns>The assigned slot, or <see langword="null" /> when no slot is available.</returns>
	/// <exception cref="InvalidOperationException">
	///     The user is banned, the session is already seated in another room, or the room is full.
	/// </exception>
	internal RoomSlot? Seat(GameSession session)
	{
		if (Room.Banned.Contains(session.User))
			throw new InvalidOperationException("The user is banned from this room.");

		if (session.Slot is { } existing)
			return ReferenceEquals(existing.Slots, this)
				? existing
				: throw new InvalidOperationException("The session is already seated in another room.");

		var slot = _slots.FirstOrDefault(s => s is { Locked: false, Session: null });
		if (slot is null) return null;

		slot.Occupy(session);

		if (Room.TeamType.NeedSplitTeam())
		{
			var redCount = _slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = _slots.Count(s => s.Team == GameTeam.Blue);
			slot.SetTeam(redCount <= blueCount ? GameTeam.Red : GameTeam.Blue);
		}

		Room.Emit(new PlayerJoined(Room, session, slot));
		return slot;
	}

	/// <summary>Clears the slot occupied by <paramref name="session" />, if any.</summary>
	/// <param name="session">The session to remove.</param>
	/// <returns>The slot that was vacated, or <see langword="null" /> when the session was not seated.</returns>
	internal RoomSlot? Vacate(GameSession session)
	{
		var slot = Find(session);
		if (slot is null) return null;

		if (session.Equals(Room.Host))
			Room.SetHostSilently(null);

		slot.Clear();
		return slot;
	}

	/// <summary>Moves a player from one slot to another.</summary>
	/// <param name="from">The slot the player currently occupies.</param>
	/// <param name="to">The destination slot.</param>
	/// <remarks>This is a referee operation, so it is allowed while the room is locked.</remarks>
	/// <exception cref="InvalidOperationException">The move is not allowed.</exception>
	public static void Move(RoomSlot from, RoomSlot to)
	{
		if (from.Index == to.Index) return;
		var session = from.Session ?? throw new InvalidOperationException("The source slot has no player.");

		from.MoveTo(to);
		from.Slots.Room.Emit(new PlayerMoved(from.Slots.Room, session, from, to));
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