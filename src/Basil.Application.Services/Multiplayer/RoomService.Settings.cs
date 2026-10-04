using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Multiplayer;

internal sealed partial class RoomService
{
	private RoomResult Configure(Room room, Connection by, RoomSettingsChange change)
	{
		if (!RoomRules.IsHostOrManager(room, by)) return RoomResult.NotAuthorized;
		if (room.InProgress) return RoomResult.InProgress;

		if (change.IsPrivate is not null && !RoomRules.CanManage(room, by)) return RoomResult.NotAuthorized;

		if ((change.Name is not null && string.IsNullOrWhiteSpace(change.Name)) ||
		    change.Size is < 1 or > RoomSlots.MaxSlotCount || change is { ClearBeatmap: true, Beatmap: not null } ||
		    (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
		    (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
		    (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)))
			return RoomResult.InvalidSettings;

		if (change == new RoomSettingsChange()) return RoomResult.Ok;

		var mode = change.Mode ?? room.Mode;
		var freemods = change.Freemods ?? room.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomResult.InvalidMods;

		var countdownCancelled = room.CountdownStartsRound &&
		                         (change.Beatmap is not null || change.ClearBeatmap || change.Mode is not null ||
		                          change.Mods is not null || change.Freemods is not null ||
		                          change.TeamType is not null || change.WinCondition is not null);
		if (countdownCancelled) RoundMechanics.StopCountdown(room);

		if (change.Name is { } name) room.Match.Value.Name = name;

		if (change.Password is { } password) room.Password = password;
		if (change.Beatmap is not null || change.ClearBeatmap)
		{
			room.Beatmap = change.Beatmap;
			foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Ready))
				RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.NotReady);
		}

		if (change.Mode is { } newMode)
		{
			room.Settings.Mods = room.Settings.Mods.RemoveInvalidMods(newMode);
			room.Settings.Mode = newMode;
			foreach (var slot in room.Slots.Where(s => s.Mods is not null))
				slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
		}

		if (change.Freemods is { } newFreemods && newFreemods != room.Freemods) ApplyFreemods(room, newFreemods);
		if (change.Mods is { } newMods)
		{
			room.Settings.Mods = freemods ? newMods & GameMods.SpeedChangingMods : newMods;
			// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
			if (freemods && by is BanchoConnection caller && room.Slots.Find(caller) is { } callerSlot)
				callerSlot.Mods = newMods & ~GameMods.SpeedChangingMods;
		}

		if (change.TeamType is { } teamType) ApplyTeamType(room, teamType);
		if (change.WinCondition is { } winCondition) room.Settings.WinCondition = winCondition;
		if (change.Size is { } size) RoomSlotsMechanics.Resize(room.Slots, size);
		if (change.IsPrivate is { } isPrivate) room.Match.Value.IsPrivate = isPrivate;

		Emit(new RoomSettingsChanged(room, change with { Password = null }, change.Password is not null,
			countdownCancelled));
		return RoomResult.Ok;
	}

	/// <summary>Applies the room's freemod setting to the room's own mods and every seated player.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">Whether freemod is on.</param>
	/// <remarks>
	///     Turning freemod on hands the room's non-speed-changing mods to every seated player and
	///     leaves the room with only its speed-changing mods. Turning it off gives the room its
	///     speed-changing mods plus the host's own mods, and clears every player's own mods.
	/// </remarks>
	private static void ApplyFreemods(Room room, bool value)
	{
		room.Settings.Freemods = value;

		var seated = room.Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = room.Settings.Mods & ~GameMods.SpeedChangingMods;
			room.Settings.Mods &= GameMods.SpeedChangingMods;
		}
		else
		{
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (room.Host is { } host ? room.Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			room.Settings.Mods = (room.Settings.Mods & GameMods.SpeedChangingMods) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	/// <summary>Applies the room's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">The team arrangement to set.</param>
	private static void ApplyTeamType(Room room, GameTeamType value)
	{
		room.Settings.TeamType = value;

		if (value.NeedSplitTeam())
		{
			var redCount = 0;
			var blueCount = 0;

			foreach (var slot in room.Slots.Where(s => s.Player is not null))
			{
				var team = redCount <= blueCount ? GameTeam.Red : GameTeam.Blue;
				slot.Team = team;

				if (team == GameTeam.Red)
					redCount++;
				else
					blueCount++;
			}
		}
		else
		{
			foreach (var slot in room.Slots.Where(s => s.Player is not null))
				slot.Team = null;
		}
	}
}