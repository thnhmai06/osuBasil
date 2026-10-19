using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Matches;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>Settings operations on a room, scoped to a connection with its permissions.</summary>
public sealed class ScopedRoomSettings
{
	private readonly ScopedRoom _scoped;

	/// <summary>Creates a scoped settings service for a room.</summary>
	/// <param name="scoped">The scoped room.</param>
	internal ScopedRoomSettings(ScopedRoom scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="change">The settings to change.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, NotAuthorized, InProgress, InvalidSettings, InvalidMods or RoomClosed when the room has closed.</returns>
	/// <remarks>
	///     Nothing changes unless every field is valid. Selecting or clearing the beatmap sets ready players back to not
	///     ready.
	///     Turning freemod on moves the room's mods that are not speed-changing onto each player; turning it off gives the
	///     room the host's mods. Changing the team type reassigns teams. Changing the mode drops mods, the room's and the
	///     players', that the new mode does not allow. Under freemod, a seated caller's mods that are not speed-changing
	///     become
	///     that caller's own mods. A change with no field set does nothing.
	///     A change to the beatmap, mode, mods, freemod, team type or win condition cancels a countdown that would start the
	///     round.
	/// </remarks>
	public async Task<RoomResult> ConfigureAsync(RoomSettingsChange change,
		CancellationToken cancellationToken = default)
	{
		var room = _scoped.Room;
		var by = _scoped.By;

		// Build required permissions from the change
		var required = RoomPermissions.SettingsChange;
		if (change.Name is not null) required |= RoomPermissions.SettingsRename;
		if (change.Password is not null) required |= RoomPermissions.SettingsPassword;
		if (change.IsPrivate is not null) required |= RoomPermissions.SettingsPrivate;

		if (_scoped.Require(required) is { } denied) return denied;

		await using var scope = await room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		if (room.Rounds.InProgress) return RoomResult.InProgress;

		if ((change.Name is { } newName && !MatchData.IsValidName(newName)) ||
		    change.Size is < 1 or > RoomSlots.MaxSlotCount || change is { ClearBeatmap: true, Beatmap: not null } ||
		    (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
		    (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
		    (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)))
			return RoomResult.InvalidSettings;

		if (change == new RoomSettingsChange()) return RoomResult.Ok;

		var mode = change.Mode ?? room.Settings.MatchSettings.Mode;
		var freemods = change.Freemods ?? room.Settings.MatchSettings.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomResult.InvalidMods;

		if (change.Name is { } name) room.Match.Value.Name = name;

		if (change.Password is { } password) room.Settings.Password = password;
		if (change.Beatmap is not null || change.ClearBeatmap)
		{
			room.Settings.Beatmap = change.Beatmap;
			foreach (var slot in room.Slots.Where(s => s.Status is RoomSlotStatus.Ready))
				slot.SetStatus(RoomSlotStatus.NotReady);
		}

		if (change.Mode is { } newMode)
		{
			room.Settings.MatchSettings.Mods = room.Settings.MatchSettings.Mods.RemoveInvalidMods(newMode);
			room.Settings.MatchSettings.Mode = newMode;
			foreach (var slot in room.Slots.Where(s => s.Mods is not null))
				slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
		}

		if (change.Freemods is { } newFreemods && newFreemods != room.Settings.MatchSettings.Freemods)
			ApplyFreemods(room, newFreemods);
		if (change.Mods is { } newMods)
		{
			room.Settings.MatchSettings.Mods = freemods ? newMods & GameModsGroups.SpeedChanging : newMods;
			// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
			if (freemods && by is BanchoConnection caller && room.Slots.Find(caller) is { } callerSlot)
				callerSlot.Mods = newMods & ~GameModsGroups.SpeedChanging;
		}

		if (change.TeamType is { } teamType) ApplyTeamType(room, teamType);
		if (change.WinCondition is { } winCondition) room.Settings.MatchSettings.WinCondition = winCondition;
		if (change.Size is { } size) room.Slots.Resize(size);
		if (change.IsPrivate is { } isPrivate) room.Match.Value.IsPrivate = isPrivate;

		if (room.Rounds.Countdown is { StartsRound: true } &&
		    (change.Beatmap is not null || change.ClearBeatmap || change.Mode is not null || change.Mods is not null ||
		     change.Freemods is not null || change.TeamType is not null || change.WinCondition is not null))
			room.CancelCountdown(announce: true);

		room.Context.Events.Emit(new RoomSettingsChanged(room, change with { Password = null },
			change.Password is not null));
		return RoomResult.Ok;
	}

	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <param name="locked">The lock state to set.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok, NotAuthorized or RoomClosed when the room has closed; setting the current state again returns Ok and does
	///     nothing.
	/// </returns>
	public async Task<RoomResult> SetLockedAsync(bool locked, CancellationToken cancellationToken = default)
	{
		if (_scoped.Require(RoomPermissions.SlotsMovePlayers) is { } denied) return denied;

		await using var scope = await _scoped.Room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.RoomClosed;

		var room = _scoped.Room;
		if (room.Slots.Locked == locked) return RoomResult.Ok;

		room.Slots.Locked = locked;
		room.Context.Events.Emit(new RoomLockChanged(room, locked));
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
		room.Settings.MatchSettings.Freemods = value;

		var seated = room.Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = room.Settings.MatchSettings.Mods & ~GameModsGroups.SpeedChanging;
			room.Settings.MatchSettings.Mods &= GameModsGroups.SpeedChanging;
		}
		else
		{
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (room.Authority.Host is { } host ? room.Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			room.Settings.MatchSettings.Mods =
				(room.Settings.MatchSettings.Mods & GameModsGroups.SpeedChanging) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	/// <summary>Applies the room's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="value">The team arrangement to set.</param>
	private static void ApplyTeamType(Room room, GameTeamType value)
	{
		room.Settings.MatchSettings.TeamType = value;

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
