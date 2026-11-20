using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Bot.Commands;

internal static class MpReplies
{
	// ── room scope ─────────────────────────────────────────────────────────────────────────
	public static readonly Reply RoomPrefix = new("mp.roomPrefix", "[#{room}] ", "room");

	// ── !mp make / makeprivate ─────────────────────────────────────────────────────────────
	public static readonly Reply CreatedMatch = new("mp.createdMatch",
		"Created the match #{room} {name}{privacy, select, private { (private)} other {}}. You're now targeting it, and have been added as a referee.",
		"room", "name", "privacy");

	public static readonly Reply DefaultRoomName = new("mp.defaultRoomName", "{name}'s match", "name");

	// ── !mp join ───────────────────────────────────────────────────────────────────────────
	public static readonly Reply JoinUsage = new("mp.joinUsage", "Usage: !mp join <id> [password]");

	public static readonly Reply NoActiveRoomWithId =
		new("mp.noActiveRoomWithId", "No active match with id {room}.", "room");

	public static readonly Reply BannedFromMatch = new("mp.bannedFromMatch", "You're banned from this match.");

	public static readonly Reply JoinedMatch = new("mp.joinedMatch", "Joined match #{room} {name}", "room", "name");

	public static readonly Reply IncorrectPassword = new("mp.incorrectPassword", "Incorrect password.");

	public static readonly Reply MatchIsFull = new("mp.matchIsFull", "The match is full.");

	public static readonly Reply MatchIsLocked = new("mp.matchIsLocked", "The match is locked.");

	public static readonly Reply RoomsLocked = new("mp.roomsLocked", "Rooms are locked right now.");

	// ── !mp in ─────────────────────────────────────────────────────────────────────────────
	public static readonly Reply NotScopedToAnyMatch =
		new("mp.notScopedToAnyMatch", "You aren't targeting any match right now.");

	public static readonly Reply InUsage = new("mp.inUsage", "Usage: !mp in [match_id]");

	public static readonly Reply NoActiveRoomWithHashId = new("mp.noActiveRoomWithHashId",
		"No active match with id #{room}.", "room");

	public static readonly Reply NowTargetingMatch = new("mp.nowTargetingMatch",
		"Now targeting match #{room} {name}.", "room", "name");

	// ── !mp settings ──────────────────────────────────────────────────────────────────────
	public static readonly Reply SettingsRoomName =
		new("mp.settingsRoomName", "Room name: {name} (#{room})", "name", "room");

	public static readonly Reply SettingsBeatmap =
		new("mp.settingsBeatmap", "Beatmap: {beatmap} {name}", "beatmap", "name");

	public static readonly Reply SettingsBeatmapNotSelected =
		new("mp.settingsBeatmapNotSelected", "Beatmap: None selected");

	public static readonly Reply SettingsTeamMode = new("mp.settingsTeamMode",
		"Team mode: {teamType}, Win condition: {winCondition}", "teamType", "winCondition");

	public static readonly Reply SettingsActiveMods = new("mp.settingsActiveMods", "Active mods: {mods}", "mods");

	public static readonly Reply SettingsCreator = new("mp.settingsCreator", "Creator: #{id} {name}", "id", "name");

	public static readonly Reply SettingsPlayers = new("mp.settingsPlayers", "Players: ({count})", "count");

	public static readonly Reply SettingsSlot = new("mp.settingsSlot", "Slot {slot}  {status} {team}{id} {name}{tags}",
		"slot", "status", "team", "id", "name", "tags");

	public static readonly Reply SlotStatusNotReady = new("mp.slotStatusNotReady", "Not Ready");

	public static readonly Reply SlotStatusNoMap = new("mp.slotStatusNoMap", "No Map");

	public static readonly Reply HostTag = new("mp.hostTag", "Host");

	public static readonly Reply Freemod = new("mp.freemod", "Freemod");

	public static readonly Reply Tags = new("mp.tags", " [{tags}]", "tags");

	// ── !mp lock / unlock / size ───────────────────────────────────────────────────────────
	public static readonly Reply LockedMatch = new("mp.lockedMatch", "Locked the match");

	public static readonly Reply UnlockedMatch = new("mp.unlockedMatch", "Unlocked the match");

	public static readonly Reply SizeUsage = new("mp.sizeUsage", "Usage: !mp size <2-16>");

	public static readonly Reply ChangedMatchSize = new("mp.changedMatchSize", "Changed match to size {size}", "size");

	// ── !mp move / host / clearhost ────────────────────────────────────────────────────────
	public static readonly Reply MoveUsage = new("mp.moveUsage", "Usage: !mp move <name/id> <slot 1-16>");

	public static readonly Reply DestinationSlotNotOpen =
		new("mp.destinationSlotNotOpen", "Destination slot is not open.");

	public static readonly Reply MovedToSlot =
		new("mp.movedToSlot", "Moved {player} into slot {slot}", "player", "slot");

	public static readonly Reply HostUsage = new("mp.hostUsage", "Usage: !mp host <name/id>");

	public static readonly Reply ChangedMatchHost =
		new("mp.changedMatchHost", "Changed match host to {player}", "player");

	public static readonly Reply ClearedMatchHost = new("mp.clearedMatchHost", "Cleared match host");

	// ── !mp name / password / private ──────────────────────────────────────────────────────
	public static readonly Reply NameUsage = new("mp.nameUsage", "Usage: !mp name <text>");

	public static readonly Reply RoomNameUpdated = new("mp.roomNameUpdated", "Room name updated to \"{name}\"", "name");

	public static readonly Reply RemovedMatchPassword = new("mp.removedMatchPassword", "Removed the match password");

	public static readonly Reply ChangedMatchPassword = new("mp.changedMatchPassword", "Changed the match password");

	public static readonly Reply MatchIsPrivateNow = new("mp.matchIsPrivateNow",
		"This match is {privacy, select, private {private} other {not private}}.", "privacy");

	public static readonly Reply MatchNowPrivate = new("mp.matchNowPrivate",
		"The match is now private. It is hidden from the lobby, and only players who took part in it, its referees and server managers can view its history.");

	public static readonly Reply MatchNowPublic = new("mp.matchNowPublic", "The match is now public.");

	public static readonly Reply PrivateUsage = new("mp.privateUsage", "Usage: !mp private [0|1]");

	// ── !mp invite ─────────────────────────────────────────────────────────────────────────
	public static readonly Reply InviteUsage = new("mp.inviteUsage", "Usage: !mp invite <name/id>");

	public static readonly Reply UserAlreadyInRoom = new("mp.userAlreadyInRoom", "User is already in the room");

	public static readonly Reply InvitedToRoom = new("mp.invitedToRoom", "Invited {player} to the room", "player");

	// ── !mp addref / removeref / listrefs ──────────────────────────────────────────────────
	public static readonly Reply AddRefUsage = new("mp.addRefUsage", "Usage: !mp addref <name/id>");

	public static readonly Reply UserNotFound = new("mp.userNotFound", "User not found");

	public static readonly Reply
		AddedReferee = new("mp.addedReferee", "Added {player} to the match referees", "player");

	public static readonly Reply RemoveRefUsage = new("mp.removeRefUsage", "Usage: !mp removeref <name/id>");

	public static readonly Reply RemovedReferee =
		new("mp.removedReferee", "Removed {player} from the match referees", "player");

	public static readonly Reply NoReferees = new("mp.noReferees", "No referees");

	public static readonly Reply MatchReferees = new("mp.matchReferees", "Match referees:");

	public static readonly Reply NoBannedPlayers = new("mp.noBannedPlayers", "No banned players");

	public static readonly Reply MatchBans = new("mp.matchBans", "Match bans:");

	// ── !mp team / set ─────────────────────────────────────────────────────────────────────
	public static readonly Reply TeamUsage = new("mp.teamUsage", "Usage: !mp team <name/id> <red|blue>");

	public static readonly Reply MovedToTeam = new("mp.movedToTeam", "Moved {player} to team {team}", "player", "team");

	public static readonly Reply SetUsage = new("mp.setUsage",
		"Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 2-16]");

	public static readonly Reply ChangedMatchSettings = new("mp.changedMatchSettings",
		"Changed match settings to {teamType}, {winCondition}{size, select, none {.} other {, {size} slots.}}",
		"teamType", "winCondition", "size");

	// ── !mp map / mods ─────────────────────────────────────────────────────────────────────
	public static readonly Reply MapUsage = new("mp.mapUsage", "Usage: !mp map <beatmap id> [playmode]");

	public static readonly Reply NoBeatmapWithId =
		new("mp.noBeatmapWithId", "No beatmap with ID {beatmap} found.", "beatmap");

	public static readonly Reply ChangedBeatmap = new("mp.changedBeatmap",
		"Changed beatmap to {artist} - {title} [{version}]",
		"artist", "title", "version");

	public static readonly Reply InvalidMods = new("mp.invalidMods", "Invalid mods.");

	public static readonly Reply ModsUsage = new("mp.modsUsage", "Usage: !mp mods <mods>|Freemod|None");

	public static readonly Reply EnabledMods = new("mp.enabledMods", "Enabled {mods}", "mods");

	public static readonly Reply DisabledMods = new("mp.disabledMods", "Disabled {mods}", "mods");

	public static readonly Reply DisabledFreemod = new("mp.disabledFreemod", "Disabled FreeMod");

	public static readonly Reply EnabledFreemod = new("mp.enabledFreemod", "Enabled FreeMod");

	public static readonly Reply NoModChanges = new("mp.noModChanges", "No mod changes");

	// ── !mp start / timer / aborttimer / abort ─────────────────────────────────────────────
	public static readonly Reply MatchAlreadyInProgress =
		new("mp.matchAlreadyInProgress", "Match is already in progress.");

	public static readonly Reply MatchStarted = new("mp.matchStarted", "Match started");

	public static readonly Reply TimerUsage = new("mp.timerUsage", "Usage: !mp timer [seconds]");

	public static readonly Reply NoCountdownRunning = new("mp.noCountdownRunning", "No countdown is running.");

	public static readonly Reply MatchNotInProgress = new("mp.matchNotInProgress", "Match is not in progress.");

	public static readonly Reply AbortedMatch = new("mp.abortedMatch", "Aborted the match");

	// ── !mp kick / ban / unban / close ─────────────────────────────────────────────────────
	public static readonly Reply KickUsage = new("mp.kickUsage", "Usage: !mp kick <name/id>");

	public static readonly Reply
		KickedFromMatch = new("mp.kickedFromMatch", "Kicked {player} from the match", "player");

	public static readonly Reply BanUsage = new("mp.banUsage", "Usage: !mp ban <name/id>");

	public static readonly Reply UserNotRegistered = new("mp.userNotRegistered", "User is not registered.");

	public static readonly Reply BannedPlayerFromMatch = new("mp.bannedPlayerFromMatch",
		"Banned {player} from the match",
		"player");

	public static readonly Reply UnbanUsage = new("mp.unbanUsage", "Usage: !mp unban <name/id>");

	public static readonly Reply UnbannedFromMatch =
		new("mp.unbannedFromMatch", "Unbanned {player} from the match", "player");

	public static readonly Reply ClosedMatch = new("mp.closedMatch", "Closed the match");

	// ── dispatch errors ────────────────────────────────────────────────────────────────────
	public static readonly Reply UnknownMpSubcommand = new("mp.unknownMpSubcommand",
		"Unknown !mp subcommand: {subcommand}. Use !mp help to list available subcommands.", "subcommand");

	public static readonly Reply MpInDmOnly = new("mp.mpInDmOnly", "!mp in only works in a DM to BasilBot.");

	public static readonly Reply NotInARoom = new("mp.notInARoom", "You are not in a match.");

	public static readonly Reply GenericFailed = new("mp.genericFailed", "Couldn't do that ({outcome}).", "outcome");

	// ── outcome replies ───────────────────────────────────────────────────────────────────
	public static readonly Reply MissingPrivileges = new("mp.missingPrivileges",
		"You do not have the privileges needed for that.");

	public static readonly Reply AlreadyInThisMatch = new("mp.alreadyInThisMatch", "You're already in this match.");

	public static readonly Reply TargetInAnotherMatch =
		new("mp.targetInAnotherMatch", "That player is in another match.");

	public static readonly Reply TargetIsObserver = new("mp.targetIsObserver",
		"That player is watching this match as an observer.");

	public static readonly Reply TargetNotInMatch = new("mp.targetNotInMatch", "That player is not in this match.");

	public static readonly Reply TargetIsManager = new("mp.targetIsManager",
		"That can't be done to the match's creator or referees.");

	public static readonly Reply TargetNotBanned =
		new("mp.targetNotBanned", "That player is not banned from this match.");

	public static readonly Reply TargetIsBanned =
		new("mp.targetIsBanned", "That player is banned from this match. Unban them first.");

	public static readonly Reply TargetNotOnline = new("mp.targetNotOnline", "That player is not online.");

	public static readonly Reply CannotInvite = new("mp.cannotInvite", "That player cannot be invited by you.");

	public static readonly Reply TargetAlreadyReferee = new("mp.targetAlreadyReferee",
		"That user is already a referee of this match.");

	public static readonly Reply TargetNotReferee =
		new("mp.targetNotReferee", "That user is not a referee of this match.");

	public static readonly Reply TargetIsCreator = new("mp.targetIsCreator", "That user created this match.");

	public static readonly Reply TargetIsPlayer = new("mp.targetIsPlayer", "That user is playing in this match.");

	public static readonly Reply TargetNotObserver =
		new("mp.targetNotObserver", "That user is not watching this match.");

	public static readonly Reply RoomLimitReached = new("mp.roomLimitReached",
		"You already have the most matches open that you can.");

	public static readonly Reply NoMatchesAvailable =
		new("mp.noMatchesAvailable", "No more matches can be opened right now.");

	public static readonly Reply OwnSlotLocked = new("mp.ownSlotLocked", "You can't lock your own slot.");

	public static readonly Reply MatchHasNoTeams = new("mp.matchHasNoTeams", "This match has no teams.");

	public static readonly Reply FreemodNotEnabled = new("mp.freemodNotEnabled", "FreeMod is not enabled.");

	public static readonly Reply SpeedModsMatchWide = new("mp.speedModsMatchWide",
		"Speed-changing mods can only be set for the whole match.");

	public static readonly Reply TagNotAvailable =
		new("mp.tagNotAvailable", "Tag team types are only available in osu!.");

	public static readonly Reply PlayerReady =
		new("mp.playerReady", "Mods can't be changed while the player is ready.");

	public static readonly Reply SettingsInvalid = new("mp.settingsInvalid", "Those settings are not valid.");

	public static readonly Reply NoBeatmapSelected = new("mp.noBeatmapSelected", "No beatmap is selected.");

	public static readonly Reply TargetNotPlaying = new("mp.targetNotPlaying", "That player is not playing.");

	public static readonly Reply CountdownOutOfRange = new("mp.countdownOutOfRange",
		"The countdown must be between 1 second and 1 hour.");

	public static readonly Reply ScoreNotCurrentRound =
		new("mp.scoreNotCurrentRound", "That score is not from the current round.");

	public static readonly Reply MatchClosed = new("mp.matchClosed", "The match is closed.");

	public static string Describe(IReplies replies, RoomActionResult result)
	{
		return result == RoomActionResult.Denied
			? replies.Text(MissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static string Describe(IReplies replies, LobbyResult result)
	{
		return result == LobbyResult.Denied
			? replies.Text(MissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static Reply Of(LobbyResult outcome)
	{
		return outcome switch
		{
			LobbyResult.AlreadyInRoom => UserAlreadyInRoom,
			LobbyResult.TooManyRooms => RoomLimitReached,
			LobbyResult.NoRoomId => NoMatchesAvailable,
			LobbyResult.Locked => RoomsLocked,
			_ => GenericFailed
		};
	}

	public static string Describe(IReplies replies, RoomJoinResult result)
	{
		return result == RoomJoinResult.Denied
			? replies.Text(MissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static Reply Of(RoomJoinResult outcome)
	{
		return outcome switch
		{
			RoomJoinResult.RoomClosed => MatchClosed,
			RoomJoinResult.NotInRoom => TargetNotInMatch,
			RoomJoinResult.Banned => BannedFromMatch,
			RoomJoinResult.Full => MatchIsFull,
			RoomJoinResult.AlreadySeated => AlreadyInThisMatch,
			RoomJoinResult.InAnotherRoom => TargetInAnotherMatch,
			RoomJoinResult.IsObserver => TargetIsObserver,
			RoomJoinResult.IsPlayer => TargetIsPlayer,
			RoomJoinResult.NotObserver => TargetNotObserver,
			RoomJoinResult.WrongPassword => IncorrectPassword,
			_ => GenericFailed
		};
	}

	public static string Describe(IReplies replies, RoomInviteResult result)
	{
		return result == RoomInviteResult.Denied
			? replies.Text(MissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static Reply Of(RoomInviteResult outcome)
	{
		return outcome switch
		{
			RoomInviteResult.RoomClosed => MatchClosed,
			RoomInviteResult.TargetOffline => TargetNotOnline,
			RoomInviteResult.AlreadyInRoom => UserAlreadyInRoom,
			RoomInviteResult.Blocked => CannotInvite,
			_ => GenericFailed
		};
	}

	public static string Describe(IReplies replies, RoomTargetResult result)
	{
		return result == RoomTargetResult.Denied
			? replies.Text(MissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static Reply Of(RoomTargetResult outcome)
	{
		return outcome switch
		{
			RoomTargetResult.NotFound => UserNotFound,
			RoomTargetResult.RoomClosed => MatchClosed,
			RoomTargetResult.InProgress => MatchAlreadyInProgress,
			RoomTargetResult.NotInRoom => TargetNotInMatch,
			RoomTargetResult.NotBanned => TargetNotBanned,
			RoomTargetResult.IsManager => TargetIsManager,
			RoomTargetResult.IsCreator => TargetIsCreator,
			RoomTargetResult.AlreadyReferee => TargetAlreadyReferee,
			RoomTargetResult.NotReferee => TargetNotReferee,
			RoomTargetResult.SlotNotOpen => DestinationSlotNotOpen,
			RoomTargetResult.RoomLocked => MatchIsLocked,
			RoomTargetResult.NoTeams => MatchHasNoTeams,
			RoomTargetResult.Banned => TargetIsBanned,
			RoomTargetResult.TargetOffline => TargetNotOnline,
			_ => GenericFailed
		};
	}

	public static Reply Of(RoomActionResult outcome)
	{
		return outcome switch
		{
			RoomActionResult.Denied => MissingPrivileges,
			RoomActionResult.Banned => BannedFromMatch,
			RoomActionResult.Full => MatchIsFull,
			RoomActionResult.NotInRoom => TargetNotInMatch,
			RoomActionResult.IsManager => TargetIsManager,
			RoomActionResult.NotBanned => TargetNotBanned,
			RoomActionResult.AlreadyReferee => TargetAlreadyReferee,
			RoomActionResult.NotReferee => TargetNotReferee,
			RoomActionResult.IsCreator => TargetIsCreator,
			RoomActionResult.SlotNotOpen => DestinationSlotNotOpen,
			RoomActionResult.RoomLocked => MatchIsLocked,
			RoomActionResult.InProgress => MatchAlreadyInProgress,
			RoomActionResult.OwnSlot => OwnSlotLocked,
			RoomActionResult.NoTeams => MatchHasNoTeams,
			RoomActionResult.NotFreemod => FreemodNotEnabled,
			RoomActionResult.SpeedModNotAllowed => SpeedModsMatchWide,
			RoomActionResult.TagNotAvailable => TagNotAvailable,
			RoomActionResult.PlayerReady => PlayerReady,
			RoomActionResult.NoBeatmap => NoBeatmapSelected,
			RoomActionResult.NotPlaying => TargetNotPlaying,
			RoomActionResult.NotInProgress => MatchNotInProgress,
			RoomActionResult.RoundMismatch => ScoreNotCurrentRound,
			RoomActionResult.RoomClosed => MatchClosed,
			_ => GenericFailed
		};
	}
}