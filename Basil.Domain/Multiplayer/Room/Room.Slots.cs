using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public readonly record struct SlotMove(int From, int To);

public sealed partial class Room
{
	public (RoomActionResult Outcome, SlotMove? Move) ChangeSlot(UserRecord player, int index)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Slots.Find(player) is not { } from) return (RoomActionResult.NotInRoom, null);
		if (Slots.Locked) return (RoomActionResult.RoomLocked, null);
		if (Rounds.InProgress) return (RoomActionResult.InProgress, null);
		if (Slots.At(index) is not { } to) return (RoomActionResult.SlotNotOpen, null);
		if (ReferenceEquals(from, to)) return (RoomActionResult.Ok, null);
		if (to.Locked || to.Player is not null) return (RoomActionResult.SlotNotOpen, null);

		from.MoveTo(to);
		return (RoomActionResult.Ok, new SlotMove(from.Index, to.Index));
	}

	public (RoomActionResult Outcome, SlotMove? Move) MovePlayer(UserRecord player, int index)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Slots.Find(player) is not { } from) return (RoomActionResult.NotInRoom, null);
		if (Slots.At(index) is not { } to) return (RoomActionResult.SlotNotOpen, null);
		if (ReferenceEquals(from, to)) return (RoomActionResult.Ok, null);
		if (to.Locked || to.Player is not null) return (RoomActionResult.SlotNotOpen, null);

		from.MoveTo(to);
		return (RoomActionResult.Ok, new SlotMove(from.Index, to.Index));
	}

	public RoomActionResult ArrangeSlots(IReadOnlyList<SlotArrangement> arrangement)
	{
		if (IsClosed) return RoomActionResult.RoomClosed;
		if (Rounds.InProgress) return RoomActionResult.InProgress;

		var seated = Slots.Where(s => s.Player is not null)
			.ToDictionary(s => s.Player!, s => s.Capture());
		var named = arrangement.Where(e => e.Player is not null).Select(e => e.Player!).ToList();
		if (!IsValidArrangement(arrangement, seated, named)) throw new InvalidRoomSettingsException();

		ApplyArrangement(arrangement, seated);
		return RoomActionResult.Ok;
	}

	private static bool IsValidArrangement(IReadOnlyList<SlotArrangement> arrangement,
		Dictionary<UserRecord, RoomSlotState> seated, List<UserRecord> named)
	{
		return !(arrangement.Any(e =>
			         e.Index is < 1 or > RoomRules.MaxPlayers || (e.Player is not null && e.Locked)) ||
		         arrangement.DistinctBy(e => e.Index).Count() != arrangement.Count ||
		         named.Count != seated.Count || !seated.Keys.ToHashSet().SetEquals(named));
	}

	private void ApplyArrangement(IReadOnlyList<SlotArrangement> arrangement,
		Dictionary<UserRecord, RoomSlotState> seated)
	{
		foreach (var slot in Slots)
			slot.Clear();

		foreach (var entry in arrangement)
		{
			var slot = Slots.At(entry.Index)!;
			slot.Locked = entry.Locked;
			if (entry.Player is null) continue;

			slot.Restore(seated[entry.Player]);
			if (entry.Team is { } team && Settings.Editable.TeamType.IsTeamMode()) slot.Team = team;
		}
	}

	public (RoomActionResult Outcome, int? Changed) SetReady(UserRecord player, bool ready)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Slots.Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (Rounds.InProgress) return (RoomActionResult.InProgress, null);

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return (RoomActionResult.Ok, null);

		slot.SetStatus(status);
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, int? Changed) SetHasMap(UserRecord player, bool has)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Slots.Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (slot.Status is RoomSlotStatus.Playing || (has && slot.Status is not RoomSlotStatus.NoMap))
			return (RoomActionResult.Ok, null);

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return (RoomActionResult.Ok, null);

		slot.SetStatus(status);
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, int Slot, GameTeam Team) ToggleTeam(UserRecord player)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, 0, default);
		if (Slots.Find(player) is not { } slot) return (RoomActionResult.NotInRoom, 0, default);
		if (!Settings.Editable.TeamType.IsTeamMode()) return (RoomActionResult.NoTeams, 0, default);
		if (Slots.Locked) return (RoomActionResult.RoomLocked, 0, default);
		if (Rounds.InProgress) return (RoomActionResult.InProgress, 0, default);

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index, team);
	}

	public (RoomActionResult Outcome, int? Changed) SetTeam(UserRecord player, GameTeam team)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (!Settings.Editable.TeamType.IsTeamMode()) return (RoomActionResult.NoTeams, null);
		if (Slots.Find(player) is not { Player: not null } slot) return (RoomActionResult.NotInRoom, null);
		if (slot.Team == team) return (RoomActionResult.Ok, null);

		slot.Team = team;
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, int? Changed) SetMods(UserRecord player, GameMods mods)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, null);
		if (Slots.Find(player) is not { } slot) return (RoomActionResult.NotInRoom, null);
		if (Rounds.InProgress) return (RoomActionResult.InProgress, null);
		if (!Settings.Editable.Freemods) return (RoomActionResult.NotFreemod, null);
		if ((mods & GameModsGroups.SpeedChanging) != GameMods.NoMod) return (RoomActionResult.SpeedModNotAllowed, null);
		if (!mods.IsValid(Settings.Editable.Mode)) throw new InvalidModsException();
		if (slot.Mods == mods) return (RoomActionResult.Ok, null);

		slot.Mods = mods;
		return (RoomActionResult.Ok, slot.Index);
	}

	public (RoomActionResult Outcome, bool Changed) SetLocked(bool locked)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, false);
		if (Slots.Locked == locked) return (RoomActionResult.Ok, false);

		Slots.Locked = locked;
		return (RoomActionResult.Ok, true);
	}

	public (RoomActionResult Outcome, int Slot, bool Locked, UserRecord? Evicted, RoomRoundProgress? Progress) LockSlot(
		UserRecord? actor, int index, DateTimeOffset now)
	{
		if (IsClosed) return (RoomActionResult.RoomClosed, 0, false, null, null);
		if (Slots.At(index) is not { } slot) return (RoomActionResult.SlotNotOpen, 0, false, null, null);
		if (actor is not null && actor.Equals(slot.Player)) return (RoomActionResult.OwnSlot, 0, false, null, null);

		var locking = !slot.Locked;

		UserRecord? evicted = null;
		RoomRoundProgress? progress = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			Vacate(player);
			progress = AdvanceRound(now);
		}

		slot.Locked = locking;
		return (RoomActionResult.Ok, slot.Index, locking, evicted, progress);
	}

	public RoomSlot? Seat(UserRecord player)
	{
		if (Banned.Contains(player))
			throw new InvalidOperationException("The user is banned from this room.");

		if (Slots.Find(player) is { } existing) return existing;

		var slot = Slots.FirstOrDefault(s => s is { Locked: false, Player: null });
		if (slot is null) return null;

		slot.Occupy(player);

		if (Settings.Editable.TeamType.IsTeamMode())
		{
			var redCount = Slots.Count(s => s.Team == GameTeam.Red);
			var blueCount = Slots.Count(s => s.Team == GameTeam.Blue);
			slot.Team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
		}

		return slot;
	}

	public RoomSlot Reseat(UserRecord player)
	{
		var slot = Slots.Find(player)!;
		var team = slot.Team;

		slot.Clear();
		slot.Occupy(player);
		slot.Team = team;
		return slot;
	}

	public RoomSlot? Vacate(UserRecord player)
	{
		var slot = Slots.Find(player);
		if (slot is null) return null;

		PassHostFrom(player);

		Rounds.Settlement?.Leave(player);
		slot.Clear();
		return slot;
	}

	private void PassHostFrom(UserRecord leaving)
	{
		if (!leaving.Equals(Authority.Host)) return;
		Authority.Host = Slots
			.FirstOrDefault(slot => slot.Player is not null && !slot.Player.Equals(leaving))
			?.Player;
	}
}