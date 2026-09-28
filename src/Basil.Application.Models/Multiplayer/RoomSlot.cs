using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Models.Multiplayer;

public sealed class RoomSlot
{
	private User? _user;
	private RoomSlotStatus? _status;
	private GameTeam? _team;
	private GameMods? _mods;
	private bool? _introSkipped;

	public readonly RoomSlots Slots;
	public readonly int Index;

	public bool Locked
	{
		get;
		set
		{
			if (field == value) return;
			if (value) Clear();
			field = value;
		}
	}

	public User? User
	{
		get => _user;
		internal set
		{
			if (Equals(_user, value)) return;
			if (Locked) throw new InvalidOperationException("The slot is locked.");

			Clear();
			if (value is null) return;

			_user = value;
			_status = RoomSlotStatus.NotReady;
			_mods = GameMods.NoMod;
		}
	}

	public RoomSlotStatus? Status
	{
		get => _status;
		set
		{
			if (Equals(_status, value)) return;

			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfUndefined(v);
			}

			_status = value;
			IntroSkipped = value is RoomSlotStatus.Playing ? false : null;
		}
	}

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

	public GameTeam? Team
	{
		get => _team;
		set
		{
			if (Equals(_team, value)) return;

			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfPlaying();
				ThrowIfUndefined(v);

				if (!Slots.Room.Settings.TeamType.NeedSplitTeam())
					throw new InvalidOperationException("The room's team type does not use teams.");
				// TODO: log event
			}

			_team = value;
		}
	}

	public GameMods? Mods
	{
		get => _mods;
		set
		{
			if (Equals(_mods, value)) return;

			if (value is { } v)
			{
				ThrowIfEmpty();
				ThrowIfPlaying();
				ThrowIfUndefined(v);

				// TODO: log event
			}

			_mods = value;
		}
	}

	internal RoomSlot(RoomSlots slots, int index)
	{
		Slots = slots;
		Index = index;
	}

	internal void MoveTo(RoomSlot target)
	{
		ThrowIfDifferentRoom(target);
		if (Index == target.Index) return;
		if (Slots.Locked)
			throw new InvalidOperationException("Cannot move a player while the room is locked.");

		ThrowIfEmpty();
		target.ThrowIfLocked();

		target._user = _user;
		target._status = _status;
		target._team = _team;
		target._mods = _mods;
		target._introSkipped = _introSkipped;

		Clear();
	}

	private void Clear()
	{
		_user = null;
		_status = null;
		_team = null;
		_mods = null;
		_introSkipped = null;
	}

	public void ThrowIfEmpty()
	{
		if (_user is null)
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