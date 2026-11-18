using Basil.Domain.Game.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer;

public sealed class RoomSlot
{
	public readonly int Index;

	public readonly RoomSlots Slots;

	internal RoomSlot(RoomSlots slots, int index)
	{
		Slots = slots;
		Index = index;
	}

	public bool Locked { get; internal set; }

	public UserRecord? Player { get; internal set; }

	public RoomSlotStatus? Status { get; internal set; }

	public bool? IntroSkipped { get; internal set; }

	public bool? Loaded { get; internal set; }

	public GameTeam? Team { get; internal set; }

	public GameMods? Mods { get; internal set; }

	internal void Occupy(UserRecord player)
	{
		if (Locked) throw new InvalidOperationException("The slot is locked.");
		if (Player is not null) throw new InvalidOperationException("The slot is already occupied.");

		Player = player;
		Status = RoomSlotStatus.NotReady;
		Mods = GameMods.NoMod;
	}

	internal void MoveTo(RoomSlot target)
	{
		if (!ReferenceEquals(Slots, target.Slots))
			throw new ArgumentException("The target slot belongs to a different room.", nameof(target));
		if (Index == target.Index) return;
		if (Player is null) throw new InvalidOperationException("The slot has no player.");
		if (target.Locked || target.Player is not null)
			throw new InvalidOperationException("The target slot is not open.");

		target.Restore(Capture());
		Clear();
	}

	internal RoomSlotState Capture()
	{
		return new RoomSlotState(Player!, Status, Team, Mods, IntroSkipped, Loaded);
	}

	internal void Restore(RoomSlotState state)
	{
		Player = state.Player;
		Status = state.Status;
		Team = state.Team;
		Mods = state.Mods;
		IntroSkipped = state.IntroSkipped;
		Loaded = state.Loaded;
	}

	internal void Clear()
	{
		Player = null;
		Status = null;
		Team = null;
		Mods = null;
		IntroSkipped = null;
		Loaded = null;
	}

	internal void SetStatus(RoomSlotStatus? status)
	{
		if (Status == status) return;

		Status = status;
		IntroSkipped = status == RoomSlotStatus.Playing ? false : null;
		Loaded = status == RoomSlotStatus.Playing ? false : null;
	}
}

internal readonly record struct RoomSlotState(
	UserRecord Player,
	RoomSlotStatus? Status,
	GameTeam? Team,
	GameMods? Mods,
	bool? IntroSkipped,
	bool? Loaded);

public enum RoomSlotStatus : byte
{
	NoMap,

	NotReady,

	Ready,

	Playing,

	Complete
}