using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

public sealed class RoomSettings
{
	private readonly MatchRecord _match;
	private readonly Room _room;

	internal RoomSettings(Room room, MatchRecord match, MatchSettings matchSettings, string password)
	{
		_room = room;
		Password = password;
		_match = match;
		Editable = matchSettings;
		MatchSettings = new MatchSettingsView(matchSettings);
	}

	internal MatchSettings Editable { get; }

	public IMatchSettings MatchSettings { get; }

	public string Name => _match.Value.Name;

	public bool IsPrivate => _match.Value.IsPrivate;

	public string Password { get; internal set; }

	public BeatmapReference? Beatmap { get; internal set; }

	public (RoomActionResult Outcome, bool Changed) Configure(UserRecord? caller, RoomSettingsChange change)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);

		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, false);

		if (!AreValid(change)) throw new InvalidRoomSettingsException();

		if (change == new RoomSettingsChange()) return (RoomActionResult.Ok, false);

		var mode = change.Mode ?? Editable.Mode;
		var freemods = change.Freemods ?? Editable.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) throw new InvalidModsException();

		if (change.Name is { } name) _match.Value.Name = name;

		if (change.Password is { } password) Password = password;
		ApplyBeatmap(change);
		if (change.Mode is { } newMode) ApplyMode(newMode);
		if (change.Freemods is { } newFreemods && newFreemods != Editable.Freemods)
			ApplyFreemods(newFreemods);
		if (change.Mods is { } newMods) ApplyMods(caller, newMods, freemods);

		if (change.TeamType is { } teamType) ApplyTeamType(teamType);
		if (change.WinCondition is { } winCondition) Editable.WinCondition = winCondition;
		if (change.Size is { } size) _room.Slots.Resize(size);
		if (change.IsPrivate is { } isPrivate) _match.Value.IsPrivate = isPrivate;

		return (RoomActionResult.Ok, true);
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

		Beatmap = change.Beatmap;
		foreach (var slot in _room.Slots.Where(s => s.Status is RoomSlotStatus.Ready))
			slot.SetStatus(RoomSlotStatus.NotReady);
	}

	private void ApplyMode(GameMode newMode)
	{
		Editable.Mods = Editable.Mods.RemoveInvalidMods(newMode);
		Editable.Mode = newMode;
		foreach (var slot in _room.Slots.Where(s => s.Mods is not null))
			slot.Mods = slot.Mods!.Value.RemoveInvalidMods(newMode);
	}

	private void ApplyMods(UserRecord? caller, GameMods newMods, bool freemods)
	{
		Editable.Mods = freemods ? newMods & GameModsGroups.SpeedChanging : newMods;
		// Under freemod a seated caller keeps the rest of the mods as their own.
		if (freemods && caller is not null && _room.Slots.Find(caller) is { } callerSlot)
			callerSlot.Mods = newMods & ~GameModsGroups.SpeedChanging;
	}

	private void ApplyFreemods(bool value)
	{
		var seated = _room.Slots.Where(s => s.Player is not null).ToList();
		if (value)
		{
			// Players take the room's mods that combine per player; the room keeps only the
			// speed-changing ones, which must stay the same for everyone.
			foreach (var slot in seated)
				slot.Mods = Editable.Mods & ~GameModsGroups.SpeedChanging;
			Editable.Mods &= GameModsGroups.SpeedChanging;
			Editable.Freemods = true;
		}
		else
		{
			Editable.Freemods = false;
			// The room keeps its speed-changing mods and takes the host's own mods.
			var hostMods = (_room.Members.Host is { } host ? _room.Slots.Find(host.Record)?.Mods : null) ?? GameMods.NoMod;
			Editable.Mods =
				(Editable.Mods & GameModsGroups.SpeedChanging) | hostMods;
			foreach (var slot in seated)
				slot.Mods = GameMods.NoMod;
		}
	}

	private void ApplyTeamType(GameTeamType value)
	{
		Editable.TeamType = value;

		if (value.IsTeamMode())
		{
			var redCount = 0;
			var blueCount = 0;

			foreach (var slot in _room.Slots.Where(s => s.Player is not null))
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
			foreach (var slot in _room.Slots.Where(s => s.Player is not null))
				slot.Team = null;
		}
	}

	public bool PasswordMatches(string password)
	{
		return string.IsNullOrEmpty(Password) || Password == password;
	}

	private sealed class MatchSettingsView(MatchSettings settings) : IMatchSettings
	{
		public GameMode Mode => settings.Mode;

		public GameMods Mods => settings.Mods;

		public bool Freemods => settings.Freemods;

		public GameTeamType TeamType => settings.TeamType;

		public GameWinCondition WinCondition => settings.WinCondition;

		public int Seed => settings.Seed;
	}
}