using Basil.Domain.Beatmaps;
using Basil.Domain.Game;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Users;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>The settings of a room and the rules for changing them.</summary>
public sealed class RoomSettings
{
	private readonly MatchRecord _match;
	private readonly Room _room;

	/// <summary>Creates the settings of a room.</summary>
	/// <param name="room">The room the settings belong to.</param>
	/// <param name="match">The match the room hosts.</param>
	/// <param name="matchSettings">The settings the room starts with.</param>
	/// <param name="password">The password players must give to join; empty for none.</param>
	internal RoomSettings(Room room, MatchRecord match, MatchSettings matchSettings, string password)
	{
		_room = room;
		Password = password;
		_match = match;
		Editable = matchSettings;
		MatchSettings = new MatchSettingsView(matchSettings);
	}

	/// <summary>Gets the match settings the room changes and copies into each round.</summary>
	internal MatchSettings Editable { get; }

	/// <summary>Gets a read-only view of the match settings.</summary>
	public IMatchSettings MatchSettings { get; }

	/// <summary>Gets the name of the room.</summary>
	public string Name => _match.Value.Name;

	/// <summary>Gets whether the room is private.</summary>
	public bool IsPrivate => _match.Value.IsPrivate;

	/// <summary>Gets the password players must give to join; empty when there is none.</summary>
	public string Password { get; internal set; }

	/// <summary>Gets the room's beatmap, or <see langword="null"/> when none is chosen.</summary>
	public BeatmapReference? Beatmap { get; internal set; }

	/// <summary>Applies a change to the room's settings; refused when the room is closed or a round is in progress.</summary>
	/// <remarks>Choosing a beatmap resets ready slots to not ready. Changing the mode strips mods the new mode does not allow from the room and every slot, and turns a tag team type into the matching team type without tags, as tag team types exist only in osu!. Changing the team type to another one puts every seated player on the red team in a team mode. Changing the size locks or opens empty slots. Turning freemods on or off moves mods between the room and the players' slots.</remarks>
	/// <param name="caller">The user making the change, whose own slot keeps the non-speed mods when the room is in freemods; <see langword="null"/> when no user acts.</param>
	/// <param name="change">The changes; a member left unset keeps its current value.</param>
	/// <returns>The outcome, and whether any setting changed (false for an empty change); <see cref="RoomActionResult.TagNotAvailable"/> when a tag team type is asked for in a mode other than osu!.</returns>
	/// <exception cref="InvalidRoomSettingsException">The name is blank, the size is outside <see cref="RoomRules.MinPlayers"/> to <see cref="RoomRules.MaxPlayers"/>, a beatmap is both set and cleared, or an enum value is undefined.</exception>
	/// <exception cref="InvalidModsException">The requested mods are not valid for the mode.</exception>
	public (RoomActionResult Outcome, bool Changed) Configure(UserRecord? caller, RoomSettingsChange change)
	{
		if (_room.IsClosed) return (RoomActionResult.RoomClosed, false);
		if (_room.Rounds.InProgress) return (RoomActionResult.InProgress, false);
		if (!AreValid(change)) throw new InvalidRoomSettingsException();
		if (change == new RoomSettingsChange()) return (RoomActionResult.Ok, false);

		var mode = change.Mode ?? Editable.Mode;
		var freemods = change.Freemods ?? Editable.Freemods;
		if (change.Mods is { } requestedMods && !requestedMods.IsValid(mode)) throw new InvalidModsException();
		if (change.TeamType is { } requestedType && requestedType.IsTagMode() && mode != GameMode.Standard)
			return (RoomActionResult.TagNotAvailable, false);

		if (change.Name is { } name) _match.Value.Name = name;

		if (change.Password is { } password) Password = password;
		ApplyBeatmap(change);
		if (change.Mode is { } newMode) ApplyMode(newMode);
		if (change.Freemods is { } newFreemods && newFreemods != Editable.Freemods)
			ApplyFreemods(newFreemods);
		if (change.Mods is { } newMods) ApplyMods(caller, newMods, freemods);

		var teamType = change.TeamType ?? (mode != GameMode.Standard ? WithoutTags(Editable.TeamType) : null);
		if (teamType is { } newTeamType && newTeamType != Editable.TeamType) ApplyTeamType(newTeamType);
		if (change.WinCondition is { } winCondition) Editable.WinCondition = winCondition;
		if (change.Size is { } size) _room.Slots.Resize(size);
		if (change.IsPrivate is { } isPrivate) _match.Value.IsPrivate = isPrivate;

		return (RoomActionResult.Ok, true);
	}

	private static bool AreValid(RoomSettingsChange change)
	{
		return !((change.Name is { } newName && !MatchData.IsValidName(newName)) ||
		         change.Size is < RoomRules.MinPlayers or > RoomRules.MaxPlayers ||
		         change is { ClearBeatmap: true, Beatmap: not null } ||
		         (change.Mode is { } requestedMode && !Enum.IsDefined(requestedMode)) ||
		         (change.TeamType is { } requestedTeamType && !Enum.IsDefined(requestedTeamType)) ||
		         (change.WinCondition is { } requestedWinCondition && !Enum.IsDefined(requestedWinCondition)));
	}

	private static GameTeamType WithoutTags(GameTeamType type)
	{
		return type switch
		{
			GameTeamType.TagCoop => GameTeamType.HeadToHead,
			GameTeamType.TagTeamVs => GameTeamType.TeamVs,
			_ => type
		};
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

		foreach (var slot in _room.Slots.Where(s => s.Player is not null))
			slot.Team = value.IsTeamMode() ? GameTeam.Red : null;
	}

	/// <summary>Gets whether a password lets a player join: always when the room has no password, otherwise only when equal.</summary>
	/// <param name="password">The password the player gave.</param>
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