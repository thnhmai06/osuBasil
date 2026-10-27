using Basil.Common.Application;
using Basil.Application.Runtime.Connections;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Runtime.Multiplayer.Rooms;

/// <content>Settings transitions.</content>
public sealed partial class Room
{
	/// <summary>Locks or unlocks the room, which stops players from changing slot or team.</summary>
	/// <returns>Ok or RoomClosed; setting the current state again returns Ok and does nothing.</returns>
	internal Result<RoomOutcome> SetLocked(bool locked)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;
			if (Slots.Locked == locked) return RoomOutcome.Ok;

			Slots.Locked = locked;
			Emit(new RoomLockChanged(this, locked));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Changes the room's settings in one step.</summary>
	/// <param name="by">The connection that changes them.</param>
	/// <param name="change">The settings to change.</param>
	/// <returns>Ok, InProgress, InvalidSettings, InvalidMods or RoomClosed.</returns>
	internal Result<RoomOutcome> Configure(Connection by, RoomSettingsChange change)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			if (Rounds.InProgress) return RoomOutcome.InProgress;

			if ((change.Name is { } newName && !MatchData.IsValidName(newName)) ||
			    change.Size is < 1 or > RoomSlots.MaxSlotCount || change is { ClearBeatmap: true, Beatmap: not null } ||
			    (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
			    (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
			    (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)))
				return RoomOutcome.InvalidSettings;

			if (change == new RoomSettingsChange()) return RoomOutcome.Ok;

			var mode = change.Mode ?? Settings.MatchSettings.Mode;
			var freemods = change.Freemods ?? Settings.MatchSettings.Freemods;
			if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomOutcome.InvalidMods;

			if (change.Name is { } name) Match.Value.Name = name;

			if (change.Password is { } password) Settings.Password = password;
			if (change.Beatmap is not null || change.ClearBeatmap)
			{
				Settings.Beatmap = change.Beatmap;
				foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Ready))
					slot.SetStatus(RoomSlotStatus.NotReady);
			}

			if (change.Mode is { } newMode)
			{
				Settings.MatchSettings.Mods = Settings.MatchSettings.Mods.RemoveInvalidMods(newMode);
				Settings.MatchSettings.Mode = newMode;
				foreach (var slot in Slots.Where(s => s.Mods is not null))
					slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
			}

			if (change.Freemods is { } newFreemods && newFreemods != Settings.MatchSettings.Freemods)
				ApplyFreemods(newFreemods);
			if (change.Mods is { } newMods)
			{
				Settings.MatchSettings.Mods = freemods ? newMods & GameModsGroups.SpeedChanging : newMods;
				// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
				if (freemods && by is BanchoConnection caller && Slots.Find(caller) is { } callerSlot)
					callerSlot.Mods = newMods & ~GameModsGroups.SpeedChanging;
			}

			if (change.TeamType is { } teamType) ApplyTeamType(teamType);
			if (change.WinCondition is { } winCondition) Settings.MatchSettings.WinCondition = winCondition;
			if (change.Size is { } size) Slots.Resize(size);
			if (change.IsPrivate is { } isPrivate) Match.Value.IsPrivate = isPrivate;

			if (Rounds.Countdown is { StartsRound: true } &&
			    (change.Beatmap is not null || change.ClearBeatmap || change.Mode is not null ||
			     change.Mods is not null ||
			     change.Freemods is not null || change.TeamType is not null || change.WinCondition is not null))
				CancelCountdown(RoomCountdownCancelReason.SettingsChanged);

			Emit(new RoomSettingsChanged(this, change with { Password = null },
				change.Password is not null));
			return RoomOutcome.Ok;
		}
	}

	/// <summary>Applies the this's freemod setting to the this's own mods and every seated player.</summary>
	/// <param name="value">Whether freemod is on.</param>
	/// <remarks>
	///     Turning freemod on hands the this's non-speed-changing mods to every seated player and
	///     leaves the this with only its speed-changing mods. Turning it off gives the this its
	///     speed-changing mods plus the host's own mods, and clears every player's own mods.
	/// </remarks>
	private void ApplyFreemods(bool value)
	{
		var seated = Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the this's mods that combine per player; the this keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = Settings.MatchSettings.Mods & ~GameModsGroups.SpeedChanging;
			Settings.MatchSettings.Mods &= GameModsGroups.SpeedChanging;
			Settings.MatchSettings.Freemods = true;
		}
		else
		{
			Settings.MatchSettings.Freemods = false;
			// The this keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (Authority.Host is { } host ? Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			Settings.MatchSettings.Mods =
				(Settings.MatchSettings.Mods & GameModsGroups.SpeedChanging) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	/// <summary>Applies the this's team arrangement, reassigning the teams of the seated players.</summary>
	/// <param name="value">The team arrangement to set.</param>
	private void ApplyTeamType(GameTeamType value)
	{
		Settings.MatchSettings.TeamType = value;

		if (value.IsTeamMode())
		{
			var redCount = 0;
			var blueCount = 0;

			foreach (var slot in Slots.Where(s => s.Player is not null))
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
			foreach (var slot in Slots.Where(s => s.Player is not null))
				slot.Team = null;
		}
	}
}