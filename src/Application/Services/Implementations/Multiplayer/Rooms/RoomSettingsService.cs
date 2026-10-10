using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Match;

namespace Basil.Application.Services.Implementations.Multiplayer.Rooms;

/// <summary>Changes a room's shared settings and its lock.</summary>
internal sealed class RoomSettingsService(RoomEventStream events, TimeProvider time) : IRoomSettingsService
{
	/// <inheritdoc />
	public Task<RoomResult> ConfigureAsync(Room room, Connection by, RoomSettingsChange change,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => Configure(room, by, change), cancellationToken);
	}

	/// <inheritdoc />
	public Task<RoomResult> SetLockedAsync(Room room, Connection by, bool locked,
		CancellationToken cancellationToken = default)
	{
		return RoomScope.InScopeAsync(room, () => SetLocked(room, by, locked), cancellationToken);
	}

	private RoomResult Configure(Room room, Connection by, RoomSettingsChange change)
	{
		if (!RoomRules.IsHostOrManager(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Rounds.InProgress) return RoomResult.InProgress;

		if (change.IsPrivate is not null && !RoomRules.CanManage(room, by, time.GetUtcNow()))
			return RoomResult.NotAuthorized;

		if ((change.Name is { } newName && !MatchData.IsValidName(newName)) ||
		    change.Size is < 1 or > RoomSlots.MaxSlotCount || change is { ClearBeatmap: true, Beatmap: not null } ||
		    (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
		    (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
		    (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)))
			return RoomResult.InvalidSettings;

		if (change == new RoomSettingsChange()) return RoomResult.Ok;

		var mode = change.Mode ?? room.Settings.Mode;
		var freemods = change.Freemods ?? room.Settings.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomResult.InvalidMods;

		var countdownCancelled = room.Rounds.CountdownStartsRound &&
		                         (change.Beatmap is not null || change.ClearBeatmap || change.Mode is not null ||
		                          change.Mods is not null || change.Freemods is not null ||
		                          change.TeamType is not null || change.WinCondition is not null);
		if (countdownCancelled) RoundMechanics.StopCountdown(room);

		if (change.Name is { } name) room.Match.Value.Name = name;

		if (change.Password is { } password) room.Settings.Password = password;
		if (change.Beatmap is not null || change.ClearBeatmap)
		{
			room.Settings.Beatmap = change.Beatmap;
			foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Ready))
				RoomSlotsMechanics.SetStatus(slot, RoomSlotStatus.NotReady);
		}

		if (change.Mode is { } newMode)
		{
			room.Settings.Stored.Mods = room.Settings.Stored.Mods.RemoveInvalidMods(newMode);
			room.Settings.Stored.Mode = newMode;
			foreach (var slot in room.Slots.Where(s => s.Mods is not null))
				slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
		}

		if (change.Freemods is { } newFreemods && newFreemods != room.Settings.Freemods)
			ApplyFreemods(room, newFreemods);
		if (change.Mods is { } newMods)
		{
			room.Settings.Stored.Mods = freemods ? newMods & GameMods.SpeedChangingMods : newMods;
			// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
			if (freemods && by is BanchoConnection caller && room.Slots.Find(caller) is { } callerSlot)
				callerSlot.Mods = newMods & ~GameMods.SpeedChangingMods;
		}

		if (change.TeamType is { } teamType) ApplyTeamType(room, teamType);
		if (change.WinCondition is { } winCondition) room.Settings.Stored.WinCondition = winCondition;
		if (change.Size is { } size) RoomSlotsMechanics.Resize(room.Slots, size);
		if (change.IsPrivate is { } isPrivate) room.Match.Value.IsPrivate = isPrivate;

		events.Emit(new RoomSettingsChanged(room, change with { Password = null }, change.Password is not null,
			countdownCancelled));
		return RoomResult.Ok;
	}

	private RoomResult SetLocked(Room room, Connection by, bool locked)
	{
		if (!RoomRules.CanManage(room, by, time.GetUtcNow())) return RoomResult.NotAuthorized;
		if (room.Slots.Locked == locked) return RoomResult.Ok;

		room.Slots.Locked = locked;
		events.Emit(new RoomLockChanged(room, locked));
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
		room.Settings.Stored.Freemods = value;

		var seated = room.Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = room.Settings.Stored.Mods & ~GameMods.SpeedChangingMods;
			room.Settings.Stored.Mods &= GameMods.SpeedChangingMods;
		}
		else
		{
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (room.Authority.Host is { } host ? room.Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			room.Settings.Stored.Mods = (room.Settings.Stored.Mods & GameMods.SpeedChangingMods) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	/// <summary>Applies the room's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">The team arrangement to set.</param>
	private static void ApplyTeamType(Room room, GameTeamType value)
	{
		room.Settings.Stored.TeamType = value;

		if (value.IsTeamMode())
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