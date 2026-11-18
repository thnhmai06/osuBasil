using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed partial class Room
{
	internal RoomOutcome SetLocked(bool locked)
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

	internal RoomOutcome Configure(Session actor, RoomSettingsChange change)
	{
		lock (_sync)
		{
			if (IsClosed) return RoomOutcome.RoomClosed;

			if (Rounds.InProgress) return RoomOutcome.InProgress;

			if (!AreValid(change)) return RoomOutcome.InvalidSettings;

			if (change == new RoomSettingsChange()) return RoomOutcome.Ok;

			var mode = change.Mode ?? Settings.Editable.Mode;
			var freemods = change.Freemods ?? Settings.Editable.Freemods;
			if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) return RoomOutcome.InvalidMods;

			if (change.Name is { } name) Match.Value.Name = name;

			if (change.Password is { } password) Settings.Password = password;
			ApplyBeatmap(change);
			if (change.Mode is { } newMode) ApplyMode(newMode);
			if (change.Freemods is { } newFreemods && newFreemods != Settings.Editable.Freemods)
				ApplyFreemods(newFreemods);
			if (change.Mods is { } newMods) ApplyMods(actor, newMods, freemods);

			if (change.TeamType is { } teamType) ApplyTeamType(teamType);
			if (change.WinCondition is { } winCondition) Settings.Editable.WinCondition = winCondition;
			if (change.Size is { } size) Slots.Resize(size);
			if (change.IsPrivate is { } isPrivate) Match.Value.IsPrivate = isPrivate;

			Emit(new RoomSettingsChanged(this, change with { Password = null },
				change.Password is not null));
			return RoomOutcome.Ok;
		}
	}

	private static bool AreValid(RoomSettingsChange change)
	{
		return !((change.Name is { } newName && !MatchData.IsValidName(newName)) ||
		         change.Size is < 1 or > RoomRules.MaxPlayers ||
		         change is { ClearBeatmap: true, Beatmap: not null } ||
		         (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
		         (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
		         (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)));
	}

	private void ApplyBeatmap(RoomSettingsChange change)
	{
		if (change.Beatmap is null && !change.ClearBeatmap) return;

		Settings.Beatmap = change.Beatmap;
		foreach (var slot in Slots.Where(s => s.Status is RoomSlotStatus.Ready))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	private void ApplyMode(GameMode newMode)
	{
		Settings.Editable.Mods = Settings.Editable.Mods.RemoveInvalidMods(newMode);
		Settings.Editable.Mode = newMode;
		foreach (var slot in Slots.Where(s => s.Mods is not null))
			slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
	}

	private void ApplyMods(Session actor, GameMods newMods, bool freemods)
	{
		Settings.Editable.Mods = freemods ? newMods & GameModsGroups.SpeedChanging : newMods;
		// Under freemod a seated caller keeps the rest of the mods as their own, as the osu! client expects.
		if (freemods && actor is StableSession caller && Slots.Find(caller) is { } callerSlot)
			callerSlot.Mods = newMods & ~GameModsGroups.SpeedChanging;
	}

	private void ApplyFreemods(bool value)
	{
		var seated = Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = Settings.Editable.Mods & ~GameModsGroups.SpeedChanging;
			Settings.Editable.Mods &= GameModsGroups.SpeedChanging;
			Settings.Editable.Freemods = true;
		}
		else
		{
			Settings.Editable.Freemods = false;
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (Authority.Host is { } host ? Slots.Find(host)?.Mods : null) ?? GameMods.NoMod;
			Settings.Editable.Mods =
				(Settings.Editable.Mods & GameModsGroups.SpeedChanging) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	private void ApplyTeamType(GameTeamType value)
	{
		Settings.Editable.TeamType = value;

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