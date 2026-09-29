using Basil.Application.Models.Events;
using Basil.Application.Models.Events.Multiplayer;
using Basil.Application.Models.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Models.Multiplayer;

/// <summary>One of a room's 16 slots, holding its current occupant and per-slot settings.</summary>
public sealed class RoomSlot
{
	private RoomSlotStatus? _status;
	private GameTeam? _team;
	private GameMods? _mods;
	private bool? _introSkipped;
	private bool _locked;

	/// <summary>The slot collection this slot belongs to.</summary>
	public readonly RoomSlots Slots;

	/// <summary>The number of this slot, from 1 to 16.</summary>
	public readonly int Index;

	/// <summary>Gets or sets whether this slot is locked. Locking an occupied slot evicts its occupant.</summary>
	public bool Locked
	{
		get => _locked;
		set
		{
			if (_locked == value) return;

			GameSession? evicted = null;
			if (value && Session is { } session)
			{
				if (session.Equals(Slots.Room.Host))
					Slots.Room.SetHostSilently(null);
				evicted = session;
				Clear();
			}

			_locked = value;
			Slots.Room.Emit(new SlotLockChanged(this, value, evicted));
		}
	}

	/// <summary>Gets the session currently occupying this slot, or <see langword="null" /> when empty.</summary>
	public GameSession? Session { get; private set; }

	/// <summary>Gets or sets the occupied-state of this slot.</summary>
	public RoomSlotStatus? Status
	{
		get => _status;
		set
		{
			if (_status == value) return;

			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfUndefined(v);
			}

			_status = value;
			IntroSkipped = value is RoomSlotStatus.Playing ? false : null;
			Slots.Room.Emit(new SlotStatusChanged(this, value));
		}
	}

	/// <summary>Gets or sets whether the occupant has skipped the intro of the current beatmap.</summary>
	public bool? IntroSkipped
	{
		get => _introSkipped;
		set
		{
			if (_introSkipped == value) return;

			if (value is not null)
			{
				ThrowIfEmpty();
				if (_status is not RoomSlotStatus.Playing)
					throw new InvalidOperationException("The player is not playing.");
			}

			_introSkipped = value;
		}
	}

	/// <summary>Gets or sets the team assigned to this slot.</summary>
	public GameTeam? Team
	{
		get => _team;
		set
		{
			if (_team == value) return;

			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfPlaying();
				ThrowIfUndefined(v);

				if (!Slots.Room.TeamType.NeedSplitTeam())
					throw new InvalidOperationException("The room's team type does not use teams.");
			}

			_team = value;
			Slots.Room.Emit(new SlotTeamChanged(this, value));
		}
	}

	/// <summary>Gets or sets the mods selected for this slot, used when free mods are enabled.</summary>
	public GameMods? Mods
	{
		get => _mods;
		set
		{
			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfPlaying();
				if (!Slots.Room.Freemods)
					throw new InvalidOperationException("The room does not allow players to choose their own mods.");
				value = v.RemoveInvalidMods(Slots.Room.Mode);
			}

			if (_mods == value) return;
			_mods = value;
			Slots.Room.Emit(new SlotModsChanged(this, value));
		}
	}

	internal RoomSlot(RoomSlots slots, int index)
	{
		Slots = slots;
		Index = index;
	}

	/// <summary>Seats <paramref name="session" /> in this slot.</summary>
	/// <param name="session">The session to seat.</param>
	/// <exception cref="InvalidOperationException">The slot is locked or already occupied.</exception>
	internal void Occupy(GameSession session)
	{
		if (Locked) throw new InvalidOperationException("The slot is locked.");
		if (Session is not null) throw new InvalidOperationException("The slot is already occupied.");

		Session = session;
		session.Slot = this;
		_status = RoomSlotStatus.NotReady;
		_mods = GameMods.NoMod;
	}

	/// <summary>Clears this slot, making it empty.</summary>
	internal void Clear()
	{
		if (Session is { } session)
			session.Slot = null;

		Session = null;
		_status = null;
		_team = null;
		_mods = null;
		_introSkipped = null;
	}

	/// <summary>Sets the slot's own mods without validating the room's freemod setting or playing state.</summary>
	/// <param name="mods">The mods to set, or <see langword="null" /> to clear them.</param>
	internal void SetMods(GameMods? mods)
	{
		_mods = mods;
	}

	/// <summary>Assigns a team without validating the room's team type or playing state.</summary>
	/// <param name="team">The team to assign, or <see langword="null" /> to clear it.</param>
	internal void SetTeam(GameTeam? team)
	{
		_team = team;
	}

	/// <summary>Assigns a status without emitting a <see cref="SlotStatusChanged" /> event.</summary>
	/// <param name="status">The status to assign, or <see langword="null" /> to clear it.</param>
	internal void SetStatus(RoomSlotStatus? status)
	{
		if (_status == status) return;
		_status = status;
		_introSkipped = status is RoomSlotStatus.Playing ? false : null;
	}

	/// <summary>Locks or unlocks the slot without emitting a <see cref="SlotLockChanged" /> event.</summary>
	/// <param name="locked">The lock state to assign.</param>
	internal void SetLocked(bool locked)
	{
		_locked = locked;
	}

	internal void MoveTo(RoomSlot target)
	{
		ThrowIfDifferentRoom(target);
		if (Index == target.Index) return;
		ThrowIfEmpty();
		target.ThrowIfLocked();

		var session = Session!;
		target.Session = session;
		session.Slot = target;
		target._status = _status;
		target._team = _team;
		target._mods = _mods;
		target._introSkipped = _introSkipped;

		Session = null;
		_status = null;
		_team = null;
		_mods = null;
		_introSkipped = null;
	}

	public void ThrowIfEmpty()
	{
		if (Session is null)
			throw new InvalidOperationException("The slot has no player.");
	}

	public void ThrowIfLocked()
	{
		if (Locked)
			throw new InvalidOperationException("The slot is locked.");
	}

	public void ThrowIfPlaying()
	{
		if (_status is RoomSlotStatus.Playing)
			throw new InvalidOperationException("The player is playing.");
	}

	public void ThrowIfDifferentRoom(RoomSlots slots)
	{
		if (!ReferenceEquals(Slots, slots))
			throw new ArgumentException("The target slot belongs to a different room.", nameof(slots));
	}

	public void ThrowIfDifferentRoom(RoomSlot slot)
	{
		ThrowIfDifferentRoom(slot.Slots);
	}

	private static void ThrowIfUndefined<T>(T value) where T : struct, Enum
	{
		if (!Enum.IsDefined(value))
			throw new ArgumentOutOfRangeException(nameof(value), value, null);
	}
}

/// <summary>
///     Describes the occupied-state of a player in a multiplayer match slot.
/// </summary>
public enum RoomSlotStatus : byte
{
	/// <summary>The player does not have the current beatmap.</summary>
	NoMap,

	/// <summary>The player has the current beatmap but is not ready.</summary>
	NotReady,

	/// <summary>The player is ready to play.</summary>
	Ready,

	/// <summary>The player is currently playing the beatmap.</summary>
	Playing,

	/// <summary>The player has finished the beatmap and is waiting for the round to end.</summary>
	Complete
}