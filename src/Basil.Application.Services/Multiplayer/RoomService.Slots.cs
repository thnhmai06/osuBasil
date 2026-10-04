using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Services.Multiplayer;

internal sealed partial class RoomService
{
	private RoomResult ChangeSlot(Room room, BanchoConnection by, int index)
	{
		if (room.Slots.Find(by) is not { } from) return RoomResult.NotInRoom;
		if (room.Slots.Locked) return RoomResult.RoomLocked;
		if (room.InProgress) return RoomResult.InProgress;
		if (room.Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		RoomSlotsMechanics.MoveTo(from, to);
		Emit(new RoomPlayerMoved(room, by, from.Index, to.Index));
		return RoomResult.Ok;
	}

	private RoomResult Move(Room room, Connection by, User player, int index)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.Find(player) is not { Player: { } seated } from) return RoomResult.NotInRoom;
		if (room.Slots.At(index) is not { } to) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(from, to)) return RoomResult.Ok;
		if (to.Locked || to.Player is not null) return RoomResult.SlotNotOpen;

		RoomSlotsMechanics.MoveTo(from, to);
		Emit(new RoomPlayerMoved(room, seated, from.Index, to.Index));
		return RoomResult.Ok;
	}

	private RoomResult ArrangeSlots(Room room, Connection by, IReadOnlyList<SlotArrangement> arrangement)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.InProgress) return RoomResult.InProgress;

		var seated = room.Slots.Where(s => s.Player is not null)
			.ToDictionary(s => s.Player!.User, RoomSlotsMechanics.Capture);
		var named = arrangement.Where(e => e.Player is not null).Select(e => e.Player!).ToList();
		if (arrangement.Any(e => e.Index is < 1 or > RoomSlots.MaxSlotCount || (e.Player is not null && e.Locked)) ||
		    arrangement.DistinctBy(e => e.Index).Count() != arrangement.Count ||
		    named.Count != seated.Count || !seated.Keys.ToHashSet().SetEquals(named))
			return RoomResult.InvalidSettings;

		foreach (var slot in room.Slots)
			RoomSlotsMechanics.Clear(slot);

		foreach (var entry in arrangement)
		{
			var slot = room.Slots.At(entry.Index)!;
			slot.Locked = entry.Locked;
			if (entry.Player is null) continue;

			RoomSlotsMechanics.Restore(slot, seated[entry.Player]);
			if (entry.Team is { } team && room.TeamType.NeedSplitTeam()) slot.Team = team;
		}

		Emit(new RoomSlotsArranged(room));
		return RoomResult.Ok;
	}

	private RoomResult ToggleSlotLock(Room room, Connection by, int index)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.At(index) is not { } slot) return RoomResult.SlotNotOpen;
		if (ReferenceEquals(slot.Player, by)) return RoomResult.OwnSlot;

		var locking = !slot.Locked;

		BanchoConnection? evicted = null;
		RoomRoundProgress? progress = null;
		if (locking && slot.Player is { } player)
		{
			evicted = player;
			RoomSlotsMechanics.Vacate(room, player);
			progress = AdvanceRound(room);
		}

		slot.Locked = locking;
		Emit(new RoomSlotLockChanged(room, slot.Index, locking, evicted, room.Host, progress));

		if (evicted is not null)
		{
			LeaveChannel(room, evicted);
			ReportIfEmpty(room);
		}

		return RoomResult.Ok;
	}

	private RoomResult SetLocked(Room room, Connection by, bool locked)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (room.Slots.Locked == locked) return RoomResult.Ok;

		room.Slots.Locked = locked;
		Emit(new RoomLockChanged(room, locked));
		return RoomResult.Ok;
	}

	private RoomResult SetReady(Room room, BanchoConnection by, bool ready)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (room.InProgress) return RoomResult.InProgress;

		var status = ready ? RoomSlotStatus.Ready : RoomSlotStatus.NotReady;
		if (slot.Status == status) return RoomResult.Ok;

		RoomSlotsMechanics.SetStatus(slot, status);
		Emit(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomResult.Ok;
	}

	private RoomResult SetHasMap(Room room, BanchoConnection by, bool has)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (slot.Status is RoomSlotStatus.Playing) return RoomResult.Ok;

		if (has && slot.Status is not RoomSlotStatus.NoMap) return RoomResult.Ok;

		var status = has ? RoomSlotStatus.NotReady : RoomSlotStatus.NoMap;
		if (slot.Status == status) return RoomResult.Ok;

		RoomSlotsMechanics.SetStatus(slot, status);
		Emit(new RoomSlotStatusChanged(room, slot.Index, status));
		return RoomResult.Ok;
	}

	private RoomResult ToggleTeam(Room room, BanchoConnection by)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (!room.TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (room.Slots.Locked) return RoomResult.RoomLocked;
		if (room.InProgress) return RoomResult.InProgress;

		var team = slot.Team is GameTeam.Red ? GameTeam.Blue : GameTeam.Red;
		slot.Team = team;
		Emit(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomResult.Ok;
	}

	private RoomResult SetTeam(Room room, Connection by, User player, GameTeam team)
	{
		if (!RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;
		if (!room.TeamType.NeedSplitTeam()) return RoomResult.NoTeams;
		if (room.Slots.Find(player) is not { Player: not null } slot) return RoomResult.NotInRoom;
		if (slot.Team == team) return RoomResult.Ok;

		slot.Team = team;
		Emit(new RoomSlotTeamChanged(room, slot.Index, team));
		return RoomResult.Ok;
	}

	private RoomResult SetPlayerMods(Room room, BanchoConnection by, GameMods mods)
	{
		if (room.Slots.Find(by) is not { } slot) return RoomResult.NotInRoom;
		if (room.InProgress) return RoomResult.InProgress;
		if (!room.Freemods) return RoomResult.NotFreemod;
		if ((mods & GameMods.SpeedChangingMods) != GameMods.NoMod) return RoomResult.SpeedModNotAllowed;
		if (!mods.IsValid(room.Mode)) return RoomResult.InvalidMods;
		if (slot.Mods == mods) return RoomResult.Ok;

		slot.Mods = mods;
		Emit(new RoomSlotModsChanged(room, slot.Index, mods));
		return RoomResult.Ok;
	}
}
