using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <content>Slot transitions: moving, readiness, teams, mods and locks.</content>
public sealed partial class Room
{
	/// <summary>Moves a seated player to another open slot.</summary>
	/// <returns>Ok, NotInRoom, RoomLocked, InProgress, SlotNotOpen or RoomClosed.</returns>
	internal Result<RoomOutcome> ChangeSlot(BanchoConnection bancho, int index)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(bancho) is not { } from) return RoomOutcome.NotInRoom;
			if (Slots.Locked) return RoomOutcome.RoomLocked;
			if (Rounds.InProgress) return RoomOutcome.InProgress;
			if (Slots.At(index) is not { } to) return RoomOutcome.SlotNotOpen;
			if (ReferenceEquals(from, to)) return RoomOutcome.Ok;
			if (to.Locked || to.Player is not null) return RoomOutcome.SlotNotOpen;

			from.MoveTo(to);
			Emit(new RoomPlayerMoved(this, bancho, from.Index, to.Index));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Moves a seated player to an empty, unlocked slot on a manager's order.</summary>
	/// <returns>Ok, NotInRoom, SlotNotOpen or RoomClosed.</returns>
	internal Result<RoomOutcome> MovePlayer(UserRecord player, int index)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(player) is not { Player: { } seated } from) return RoomOutcome.NotInRoom;
			if (Slots.At(index) is not { } to) return RoomOutcome.SlotNotOpen;
			if (ReferenceEquals(from, to)) return RoomOutcome.Ok;
			if (to.Locked || to.Player is not null) return RoomOutcome.SlotNotOpen;

			from.MoveTo(to);
			Emit(new RoomPlayerMoved(this, seated, from.Index, to.Index));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Arranges every slot of the room at once.</summary>
	/// <returns>Ok, InProgress, InvalidSettings or RoomClosed.</returns>
	internal Result<RoomOutcome> ArrangeSlots(IReadOnlyList<SlotArrangement> arrangement)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Rounds.InProgress) return RoomOutcome.InProgress;

			var seated = Slots.Where(s => s.Player is not null)
				.ToDictionary(s => s.Player!.Record, s => s.Capture());
			var named = arrangement.Where(e => e.Player is not null).Select(e => e.Player!).ToList();
			if (!IsValidArrangement(arrangement, seated, named)) return RoomOutcome.InvalidSettings;

			ApplyArrangement(arrangement, seated);

			Emit(new RoomSlotsArranged(this));
			return RoomOutcome.Ok;
		}
	}

	private static bool IsValidArrangement(IReadOnlyList<SlotArrangement> arrangement,
		Dictionary<UserRecord, RoomSlotState> seated, List<UserRecord> named)
	{
		return !(arrangement.Any(e =>
			         e.Index is < 1 or > RoomSlots.MaxSlotCount || (e.Player is not null && e.Locked)) ||
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
			if (entry.Team is { } team && Settings.MatchSettings.TeamType.IsTeamMode()) slot.Team = team;
		}
	}

	/// <summary>Marks a seated player ready or not ready.</summary>
	/// <returns>Ok, NotInRoom, InProgress or RoomClosed.</returns>
	internal Result<RoomOutcome> SetReady(BanchoConnection bancho, bool ready)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
			if (Rounds.InProgress) return RoomOutcome.InProgress;

			var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
			if (slot.Status == status) return RoomOutcome.Ok;

			slot.SetStatus(status);
			Emit(new RoomSlotStatusChanged(this, slot.Index, status));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Records whether a seated player has the selected beatmap.</summary>
	/// <returns>Ok, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> SetHasMap(BanchoConnection bancho, bool has)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
			if (slot.Status is RoomSlotStatus.Playing || (has && slot.Status is not RoomSlotStatus.NoMap))
				return RoomOutcome.Ok;

			var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
			if (slot.Status == status) return RoomOutcome.Ok;

			slot.SetStatus(status);
			Emit(new RoomSlotStatusChanged(this, slot.Index, status));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Switches a seated player to the other team.</summary>
	/// <returns>Ok, NotInRoom, NoTeams, RoomLocked, InProgress or RoomClosed.</returns>
	internal Result<RoomOutcome> ToggleTeam(BanchoConnection bancho)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
			if (!Settings.MatchSettings.TeamType.IsTeamMode()) return RoomOutcome.NoTeams;
			if (Slots.Locked) return RoomOutcome.RoomLocked;
			if (Rounds.InProgress) return RoomOutcome.InProgress;

			var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
			slot.Team = team;
			Emit(new RoomSlotTeamChanged(this, slot.Index, team));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Puts a seated player on a team on a manager's order.</summary>
	/// <returns>Ok, NoTeams, NotInRoom or RoomClosed.</returns>
	internal Result<RoomOutcome> SetTeam(UserRecord player, GameTeam team)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (!Settings.MatchSettings.TeamType.IsTeamMode()) return RoomOutcome.NoTeams;
			if (Slots.Find(player) is not { Player: not null } slot) return RoomOutcome.NotInRoom;
			if (slot.Team == team) return RoomOutcome.Ok;

			slot.Team = team;
			Emit(new RoomSlotTeamChanged(this, slot.Index, team));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Chooses a seated player's own mods while freemod is on.</summary>
	/// <returns>Ok, NotInRoom, InProgress, NotFreemod, SpeedModNotAllowed, InvalidMods or RoomClosed.</returns>
	internal Result<RoomOutcome> SetMods(BanchoConnection bancho, GameMods mods)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Find(bancho) is not { } slot) return RoomOutcome.NotInRoom;
			if (Rounds.InProgress) return RoomOutcome.InProgress;
			if (!Settings.MatchSettings.Freemods) return RoomOutcome.NotFreemod;
			if ((mods & GameModsGroups.SpeedChanging) != GameMods.NoMod) return RoomOutcome.SpeedModNotAllowed;
			if (!mods.IsValid(Settings.MatchSettings.Mode)) return RoomOutcome.InvalidMods;
			if (slot.Mods == mods) return RoomOutcome.Ok;

			slot.Mods = mods;
			Emit(new RoomSlotModsChanged(this, slot.Index, mods));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Toggles whether a slot is locked, evicting a player seated in it.</summary>
	/// <returns>Ok, SlotNotOpen, OwnSlot or RoomClosed.</returns>
	internal Result<RoomOutcome> ToggleSlotLock(Connection by, int index)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.At(index) is not { } slot) return RoomOutcome.SlotNotOpen;
			if (ReferenceEquals(slot.Player, by)) return RoomOutcome.OwnSlot;

			var locking = !slot.Locked;

			BanchoConnection? evicted = null;
			RoomRoundProgress? progress = null;
			if (locking && slot.Player is { } player)
			{
				evicted = player;
				Vacate(player);
				progress = AdvanceRound();
			}

			slot.Locked = locking;
			Emit(new RoomSlotLockChanged(this, slot.Index, locking, evicted, Authority.Host,
				progress));

			if (evicted is not null)
			{
				ReportIfEmpty();
			}

			return RoomOutcome.Ok;
		}
	}
}