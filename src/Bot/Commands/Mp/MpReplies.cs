using Basil.Application.Runtime;
using Basil.Application.Runtime.Multiplayer;
using Basil.Bot.Replies;

namespace Basil.Bot.Commands.Mp;

/// <summary>
///     The user-visible reply text sent by the <c>!mp</c> chat command surface.
/// </summary>
/// <remarks>
///     Each reply has the key it has in the replies file and the wording it has unless that file changes it (the
///     strings the bot posts to chat are public behavior, so changing one is a contract change). The wording is in ICU
///     message format and names its arguments, as in <c>{room}</c>.
/// </remarks>
internal static class MpReplies
{
	// ── room scope ─────────────────────────────────────────────────────────────────────────
	/// <summary>Names the target room on each reply line posted outside that room's own channel; <c>{room}</c> is the room id.</summary>
	public static readonly Reply RoomPrefix = new("mp.roomPrefix", "[#{room}] ", "room");

	// ── !mp make / makeprivate ─────────────────────────────────────────────────────────────
	/// <summary>
	///     Reply after a room is created; <c>{room}</c> is the room id, <c>{name}</c> its name, <c>{privacy}</c> is
	///     <c>private</c> or <c>public</c>.
	/// </summary>
	public static readonly Reply CreatedMatch = new("mp.createdMatch",
		"Created the match #{room} {name}{privacy, select, private { (private)} other {}}. You're now targeting it, and have been added as a referee.",
		"room", "name", "privacy");

	/// <summary>Name of a room made without a name; <c>{name}</c> is the name of the user who made it.</summary>
	public static readonly Reply DefaultRoomName = new("mp.defaultRoomName", "{name}'s match", "name");

	// ── !mp join ───────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp join</c>.</summary>
	public static readonly Reply JoinUsage = new("mp.joinUsage", "Usage: !mp join <id> [password]");

	/// <summary>Reply when no live room carries the requested id; <c>{room}</c> is the id.</summary>
	public static readonly Reply NoActiveRoomWithId =
		new("mp.noActiveRoomWithId", "No active match with id {room}.", "room");

	/// <summary>Reply when the sender is banned from the room.</summary>
	public static readonly Reply BannedFromMatch = new("mp.bannedFromMatch", "You're banned from this match.");

	/// <summary>Reply after joining a room; <c>{room}</c> is the room id, <c>{name}</c> its name.</summary>
	public static readonly Reply JoinedMatch = new("mp.joinedMatch", "Joined match #{room} {name}", "room", "name");

	/// <summary>Reply when the supplied password is wrong.</summary>
	public static readonly Reply IncorrectPassword = new("mp.incorrectPassword", "Incorrect password.");

	/// <summary>Reply when every slot is taken.</summary>
	public static readonly Reply MatchIsFull = new("mp.matchIsFull", "The match is full.");

	/// <summary>Reply when the room is locked.</summary>
	public static readonly Reply MatchIsLocked = new("mp.matchIsLocked", "The match is locked.");

	/// <summary>Reply when the join failed for an otherwise-unreported reason.</summary>
	public static readonly Reply FailedToJoinMatch = new("mp.failedToJoinMatch", "Failed to join the match.");

	// ── !mp in ─────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when the sender has no stored scope and is not in any room.</summary>
	public static readonly Reply NotScopedToAnyMatch =
		new("mp.notScopedToAnyMatch", "You aren't targeting any match right now.");

	/// <summary>Reply when the stored scope points at a room that is no longer live; <c>{room}</c> is the id.</summary>
	public static readonly Reply WasScopedToGoneMatch = new("mp.wasScopedToGoneMatch",
		"You were targeting match #{room}, but it's no longer live.", "room");

	/// <summary>Reply reporting the current scope; <c>{room}</c> is the room id, <c>{name}</c> its name.</summary>
	public static readonly Reply CurrentlyScopedToMatch = new("mp.currentlyScopedToMatch",
		"Currently targeting match #{room} {name}.", "room", "name");

	/// <summary>Usage line for <c>!mp in</c>.</summary>
	public static readonly Reply InUsage = new("mp.inUsage", "Usage: !mp in [match_id]");

	/// <summary>Reply when the requested id names no live room; <c>{room}</c> is the id.</summary>
	public static readonly Reply NoActiveRoomWithHashId = new("mp.noActiveRoomWithHashId",
		"No active match with id #{room}.", "room");

	/// <summary>Reply after switching the scope; <c>{room}</c> is the room id, <c>{name}</c> its name.</summary>
	public static readonly Reply NowTargetingMatch = new("mp.nowTargetingMatch",
		"Now targeting match #{room} {name}.", "room", "name");

	// ── !mp settings ──────────────────────────────────────────────────────────────────────
	/// <summary>First <c>!mp settings</c> line; <c>{name}</c> is the room name, <c>{room}</c> the id.</summary>
	public static readonly Reply SettingsRoomName =
		new("mp.settingsRoomName", "Room name: {name} (#{room})", "name", "room");

	/// <summary>Beatmap line in <c>!mp settings</c>; <c>{beatmap}</c> is the map id, <c>{name}</c> its full name.</summary>
	public static readonly Reply SettingsBeatmap =
		new("mp.settingsBeatmap", "Beatmap: {beatmap} {name}", "beatmap", "name");

	/// <summary>Beatmap line in <c>!mp settings</c> when no beatmap has been selected at all.</summary>
	public static readonly Reply SettingsBeatmapNotSelected =
		new("mp.settingsBeatmapNotSelected", "Beatmap: None selected");

	/// <summary>Second <c>!mp settings</c> line; <c>{teamType}</c> is the team type, <c>{winCondition}</c> the win condition.</summary>
	public static readonly Reply SettingsTeamMode = new("mp.settingsTeamMode",
		"Team mode: {teamType}, Win condition: {winCondition}", "teamType", "winCondition");

	/// <summary>Mods line in <c>!mp settings</c>; <c>{mods}</c> is the comma-joined mod list.</summary>
	public static readonly Reply SettingsActiveMods = new("mp.settingsActiveMods", "Active mods: {mods}", "mods");

	/// <summary>Creator line in <c>!mp settings</c>; <c>{id}</c> is the user id, <c>{name}</c> the name.</summary>
	public static readonly Reply SettingsCreator = new("mp.settingsCreator", "Creator: #{id} {name}", "id", "name");

	/// <summary>Player-count line in <c>!mp settings</c>; <c>{count}</c> is the count.</summary>
	public static readonly Reply SettingsPlayers = new("mp.settingsPlayers", "Players: ({count})", "count");

	/// <summary>
	///     Line of a seated player in <c>!mp settings</c>; each argument is already padded to its column width:
	///     <c>{slot}</c> the slot number, <c>{status}</c> the slot status, <c>{team}</c> the team (or nothing), <c>{id}</c>
	///     the user id, <c>{name}</c> the user name and <c>{tags}</c> the tags (or nothing).
	/// </summary>
	public static readonly Reply SettingsSlot = new("mp.settingsSlot", "Slot {slot}  {status} {team}{id} {name}{tags}",
		"slot", "status", "team", "id", "name", "tags");

	/// <summary>Slot status shown in <c>!mp settings</c> for a player who is not ready.</summary>
	public static readonly Reply SlotStatusNotReady = new("mp.slotStatusNotReady", "Not Ready");

	/// <summary>Slot status shown in <c>!mp settings</c> for a player who lacks the beatmap.</summary>
	public static readonly Reply SlotStatusNoMap = new("mp.slotStatusNoMap", "No Map");

	/// <summary>Tag shown in <c>!mp settings</c> for the host.</summary>
	public static readonly Reply HostTag = new("mp.hostTag", "Host");

	/// <summary>Name of freemod in <c>!mp settings</c> when it is on.</summary>
	public static readonly Reply Freemod = new("mp.freemod", "Freemod");

	/// <summary>Tags after a player in <c>!mp settings</c>; <c>{tags}</c> is the slash-joined list of tags.</summary>
	public static readonly Reply Tags = new("mp.tags", " [{tags}]", "tags");

	// ── !mp lock / unlock / size ───────────────────────────────────────────────────────────
	/// <summary>Reply after locking the room.</summary>
	public static readonly Reply LockedMatch = new("mp.lockedMatch", "Locked the match");

	/// <summary>Reply after unlocking the room.</summary>
	public static readonly Reply UnlockedMatch = new("mp.unlockedMatch", "Unlocked the match");

	/// <summary>Usage line for <c>!mp size</c>.</summary>
	public static readonly Reply SizeUsage = new("mp.sizeUsage", "Usage: !mp size <1-16>");

	/// <summary>Reply after resizing the room; <c>{size}</c> is the new slot count.</summary>
	public static readonly Reply ChangedMatchSize = new("mp.changedMatchSize", "Changed match to size {size}", "size");

	// ── !mp move / host / clearhost ────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp move</c>.</summary>
	public static readonly Reply MoveUsage = new("mp.moveUsage", "Usage: !mp move <name/id> <slot 1-16>");

	/// <summary>Reply when the destination slot is occupied or otherwise not open.</summary>
	public static readonly Reply DestinationSlotNotOpen =
		new("mp.destinationSlotNotOpen", "Destination slot is not open.");

	/// <summary>Reply after moving a player; <c>{player}</c> is the player's name, <c>{slot}</c> the destination slot.</summary>
	public static readonly Reply MovedToSlot =
		new("mp.movedToSlot", "Moved {player} into slot {slot}", "player", "slot");

	/// <summary>Usage line for <c>!mp host</c>.</summary>
	public static readonly Reply HostUsage = new("mp.hostUsage", "Usage: !mp host <name/id>");

	/// <summary>Reply after transferring host; <c>{player}</c> is the new host's name.</summary>
	public static readonly Reply ChangedMatchHost =
		new("mp.changedMatchHost", "Changed match host to {player}", "player");

	/// <summary>Reply after clearing the host.</summary>
	public static readonly Reply ClearedMatchHost = new("mp.clearedMatchHost", "Cleared match host");

	// ── !mp name / password / private ──────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp name</c>.</summary>
	public static readonly Reply NameUsage = new("mp.nameUsage", "Usage: !mp name <text>");

	/// <summary>Reply after renaming the room; <c>{name}</c> is the new name.</summary>
	public static readonly Reply RoomNameUpdated = new("mp.roomNameUpdated", "Room name updated to \"{name}\"", "name");

	/// <summary>Reply after clearing the room password.</summary>
	public static readonly Reply RemovedMatchPassword = new("mp.removedMatchPassword", "Removed the match password");

	/// <summary>Reply after changing the room password.</summary>
	public static readonly Reply ChangedMatchPassword = new("mp.changedMatchPassword", "Changed the match password");

	/// <summary>Reply reporting the room's privacy; <c>{privacy}</c> is <c>private</c> or <c>public</c>.</summary>
	public static readonly Reply MatchIsPrivateNow = new("mp.matchIsPrivateNow",
		"This match is {privacy, select, private {private} other {not private}}.", "privacy");

	/// <summary>Reply after making the room private.</summary>
	public static readonly Reply MatchNowPrivate = new("mp.matchNowPrivate",
		"The match is now private. It is hidden from the lobby and the match lists, but anyone who knows its id can still join.");

	/// <summary>Reply after making the room public.</summary>
	public static readonly Reply MatchNowPublic = new("mp.matchNowPublic", "The match is now public.");

	/// <summary>Usage line for <c>!mp private</c>.</summary>
	public static readonly Reply PrivateUsage = new("mp.privateUsage", "Usage: !mp private [0|1]");

	// ── !mp invite ─────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp invite</c>.</summary>
	public static readonly Reply InviteUsage = new("mp.inviteUsage", "Usage: !mp invite <name/id>");

	/// <summary>Reply when the target is already seated in the room.</summary>
	public static readonly Reply UserAlreadyInRoom = new("mp.userAlreadyInRoom", "User is already in the room");

	/// <summary>Reply after inviting a player; <c>{player}</c> is the invitee's name.</summary>
	public static readonly Reply InvitedToRoom = new("mp.invitedToRoom", "Invited {player} to the room", "player");

	// ── !mp addref / removeref / listrefs ──────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp addref</c>.</summary>
	public static readonly Reply AddRefUsage = new("mp.addRefUsage", "Usage: !mp addref <name/id>");

	/// <summary>Reply when the target cannot be resolved to any user.</summary>
	public static readonly Reply UserNotFound = new("mp.userNotFound", "User not found");

	/// <summary>Reply after adding a referee; <c>{player}</c> is the new referee's name.</summary>
	public static readonly Reply
		AddedReferee = new("mp.addedReferee", "Added {player} to the match referees", "player");

	/// <summary>Usage line for <c>!mp removeref</c>.</summary>
	public static readonly Reply RemoveRefUsage = new("mp.removeRefUsage", "Usage: !mp removeref <name/id>");

	/// <summary>Reply after removing a referee; <c>{player}</c> is the removed referee's name.</summary>
	public static readonly Reply RemovedReferee =
		new("mp.removedReferee", "Removed {player} from the match referees", "player");

	/// <summary>Reply when the match has no referees.</summary>
	public static readonly Reply NoReferees = new("mp.noReferees", "No referees");

	/// <summary>Header line before the list of referees.</summary>
	public static readonly Reply MatchReferees = new("mp.matchReferees", "Match referees:");

	/// <summary>Reply when the match has no banned players.</summary>
	public static readonly Reply NoBannedPlayers = new("mp.noBannedPlayers", "No banned players");

	/// <summary>Header line before the list of banned players.</summary>
	public static readonly Reply MatchBans = new("mp.matchBans", "Match bans:");

	// ── !mp team / set ─────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp team</c>.</summary>
	public static readonly Reply TeamUsage = new("mp.teamUsage", "Usage: !mp team <name/id> <red|blue>");

	/// <summary>Reply after assigning a team; <c>{player}</c> is the player's name, <c>{team}</c> the team label.</summary>
	public static readonly Reply MovedToTeam = new("mp.movedToTeam", "Moved {player} to team {team}", "player", "team");

	/// <summary>Usage line for <c>!mp set</c>.</summary>
	public static readonly Reply SetUsage = new("mp.setUsage",
		"Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]");

	/// <summary>
	///     Reply after changing settings; <c>{teamType}</c> is the team type, <c>{winCondition}</c> the win condition,
	///     <c>{size}</c> the new slot count or <c>none</c> when the size was not given.
	/// </summary>
	public static readonly Reply ChangedMatchSettings = new("mp.changedMatchSettings",
		"Changed match settings to {teamType}, {winCondition}{size, select, none {.} other {, {size} slots.}}",
		"teamType", "winCondition", "size");

	// ── !mp map / mods ─────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp map</c>.</summary>
	public static readonly Reply MapUsage = new("mp.mapUsage", "Usage: !mp map <beatmap id> [playmode]");

	/// <summary>Reply when no beatmap carries the requested id; <c>{beatmap}</c> is the id.</summary>
	public static readonly Reply NoBeatmapWithId =
		new("mp.noBeatmapWithId", "No beatmap with ID {beatmap} found.", "beatmap");

	/// <summary>Reply after changing the map; <c>{artist}</c> artist, <c>{title}</c> title, <c>{version}</c> version.</summary>
	public static readonly Reply ChangedBeatmap = new("mp.changedBeatmap",
		"Changed beatmap to {artist} - {title} [{version}]",
		"artist", "title", "version");

	/// <summary>Reply when the requested mod combination is invalid for the room's mode.</summary>
	public static readonly Reply InvalidMods = new("mp.invalidMods", "Invalid mods.");

	/// <summary>Usage line for <c>!mp mods</c>.</summary>
	public static readonly Reply ModsUsage = new("mp.modsUsage", "Usage: !mp mods <mods>|Freemod|None");

	/// <summary>Mod-change summary, enabled case; <c>{mods}</c> is the mod list.</summary>
	public static readonly Reply EnabledMods = new("mp.enabledMods", "Enabled {mods}", "mods");

	/// <summary>Mod-change summary, disabled case; <c>{mods}</c> is the mod list.</summary>
	public static readonly Reply DisabledMods = new("mp.disabledMods", "Disabled {mods}", "mods");

	/// <summary>Mod-change summary line when freemod was turned off.</summary>
	public static readonly Reply DisabledFreemod = new("mp.disabledFreemod", "Disabled FreeMod");

	/// <summary>Mod-change summary line when freemod was turned on.</summary>
	public static readonly Reply EnabledFreemod = new("mp.enabledFreemod", "Enabled FreeMod");

	/// <summary>Mod-change summary when nothing changed.</summary>
	public static readonly Reply NoModChanges = new("mp.noModChanges", "No mod changes");

	// ── !mp start / timer / aborttimer / abort ─────────────────────────────────────────────
	/// <summary>Reply when the match is already in progress.</summary>
	public static readonly Reply MatchAlreadyInProgress =
		new("mp.matchAlreadyInProgress", "Match is already in progress.");

	/// <summary>Reply after starting the match immediately.</summary>
	public static readonly Reply MatchStarted = new("mp.matchStarted", "Match started");

	/// <summary>Usage line for <c>!mp timer</c>.</summary>
	public static readonly Reply TimerUsage = new("mp.timerUsage", "Usage: !mp timer [seconds]");

	/// <summary>Reply when no countdown is running.</summary>
	public static readonly Reply NoCountdownRunning = new("mp.noCountdownRunning", "No countdown is running.");

	/// <summary>Reply after aborting a countdown.</summary>
	public static readonly Reply CountdownAborted = new("mp.countdownAborted", "Countdown aborted");

	/// <summary>Reply when the match is not in progress.</summary>
	public static readonly Reply MatchNotInProgress = new("mp.matchNotInProgress", "Match is not in progress.");

	/// <summary>Reply after aborting the match.</summary>
	public static readonly Reply AbortedMatch = new("mp.abortedMatch", "Aborted the match");

	// ── !mp kick / ban / unban / close ─────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp kick</c>.</summary>
	public static readonly Reply KickUsage = new("mp.kickUsage", "Usage: !mp kick <name/id>");

	/// <summary>Reply after kicking a player; <c>{player}</c> is the kicked player's name.</summary>
	public static readonly Reply
		KickedFromMatch = new("mp.kickedFromMatch", "Kicked {player} from the match", "player");

	/// <summary>Usage line for <c>!mp ban</c>.</summary>
	public static readonly Reply BanUsage = new("mp.banUsage", "Usage: !mp ban <name/id>");

	/// <summary>Reply when the target is not a registered user.</summary>
	public static readonly Reply UserNotRegistered = new("mp.userNotRegistered", "User is not registered.");

	/// <summary>Reply after banning a player; <c>{player}</c> is the banned player's name.</summary>
	public static readonly Reply BannedPlayerFromMatch = new("mp.bannedPlayerFromMatch",
		"Banned {player} from the match",
		"player");

	/// <summary>Usage line for <c>!mp unban</c>.</summary>
	public static readonly Reply UnbanUsage = new("mp.unbanUsage", "Usage: !mp unban <name/id>");

	/// <summary>Reply after unbanning a player; <c>{player}</c> is the unbanned player's name.</summary>
	public static readonly Reply UnbannedFromMatch =
		new("mp.unbannedFromMatch", "Unbanned {player} from the match", "player");

	/// <summary>Reply after closing the match.</summary>
	public static readonly Reply ClosedMatch = new("mp.closedMatch", "Closed the match");

	// ── dispatch errors ────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a subcommand name is not recognized; <c>{subcommand}</c> is the subcommand.</summary>
	public static readonly Reply UnknownMpSubcommand = new("mp.unknownMpSubcommand",
		"Unknown !mp subcommand: {subcommand}. Use !mp help to list available subcommands.", "subcommand");

	/// <summary>Reply when a subcommand is limited to the room's creator; <c>{subcommand}</c> is the subcommand.</summary>
	public static readonly Reply CreatorOnlyMp = new("mp.creatorOnlyMp",
		"Only the match's creator can run {subcommand}.",
		"subcommand");

	/// <summary>Reply when <c>!mp in</c> is run from a channel instead of a DM to the bot.</summary>
	public static readonly Reply MpInDmOnly = new("mp.mpInDmOnly", "!mp in only works in a DM to BasilBot.");

	/// <summary>Reply when the sender is not in any room and tried a non-command-scoped subcommand.</summary>
	public static readonly Reply NotInARoom = new("mp.notInARoom", "You are not in a match.");

	/// <summary>Generic fallback reply when no specific outcome mapping exists; <c>{outcome}</c> is the outcome name.</summary>
	public static readonly Reply GenericFailed = new("mp.genericFailed", "Couldn't do that ({outcome}).", "outcome");

	// ── help text ─────────────────────────────────────────────────────────────────────────
	/// <summary>The <c>!mp help</c> listing, one subcommand per line.</summary>
	public static readonly Reply HelpText = new("mp.helpText",
		"!mp settings - show match id, map, team type, win condition, mods, and slots" + "\n" +
		"!mp lock - lock the slots, so players cannot change slot or team" + "\n" +
		"!mp unlock - unlock the room" + "\n" +
		"!mp private [0|1] - show or set the room's private status (hidden from the lobby and the match lists)" + "\n" +
		"!mp size <1-16> - set the number of available slots" + "\n" +
		"!mp move <name/id> <slot 1-16> - move a user to another slot" + "\n" +
		"!mp host <name/id> - transfer host to another user" + "\n" +
		"!mp clearhost - clear the current host" + "\n" +
		"!mp name <text> - rename the match" + "\n" +
		"!mp password [text] - set the room password; omit to clear it" + "\n" +
		"!mp invite <name/id> - invite an online user" + "\n" +
		"!mp addref <name/id> - add a referee (creator only)" + "\n" +
		"!mp removeref <name/id> - remove a referee (creator only)" + "\n" +
		"!mp listrefs - list current referees" + "\n" +
		"!mp banlist - list players banned from this match" + "\n" +
		"!mp team <name/id> <red|blue> - assign a user's team" + "\n" +
		"!mp map <beatmap id> [playmode] - change the selected map" + "\n" +
		"!mp mods <mods>|Freemod|None - set the match mods" + "\n" +
		"!mp set <teammode 0-3> [scoremode 0-3] [size 1-16] - set team type, win condition, and size at once" + "\n" +
		"!mp start [seconds] - start now, or after a countdown" + "\n" +
		"!mp timer [seconds] - start a countdown without auto-starting" + "\n" +
		"!mp aborttimer - cancel a running countdown" + "\n" +
		"!mp abort - abort the match in progress" + "\n" +
		"!mp kick <name/id> - remove a user from the room" + "\n" +
		"!mp ban <name/id> - kick and block a user from rejoining" + "\n" +
		"!mp unban <name/id> - allow a banned user to rejoin" + "\n" +
		"!mp close - close the match immediately");

	// ── outcome replies ───────────────────────────────────────────────────────────────────
	/// <summary>Reply when the actor may not perform the requested operation in this match.</summary>
	public static readonly Reply NotAuthorizedInMatch = new("mp.notAuthorizedInMatch",
		"You don't have permission to do that in this match.");

	/// <summary>Reply when the actor lacks permissions the operation needs; <c>{permissions}</c> lists them.</summary>
	public static readonly Reply MissingPermissions = new("mp.missingPermissions",
		"You are missing the permissions needed for that: {permissions}.", "permissions");

	/// <summary>Reply when the actor is already seated in this match.</summary>
	public static readonly Reply AlreadyInThisMatch = new("mp.alreadyInThisMatch", "You're already in this match.");

	/// <summary>Reply when the target is seated in a different match.</summary>
	public static readonly Reply TargetInAnotherMatch =
		new("mp.targetInAnotherMatch", "That player is in another match.");

	/// <summary>Reply when the target is watching this match as an observer.</summary>
	public static readonly Reply TargetIsObserver = new("mp.targetIsObserver",
		"That player is watching this match as an observer.");

	/// <summary>Reply when the target is not in this match.</summary>
	public static readonly Reply TargetNotInMatch = new("mp.targetNotInMatch", "That player is not in this match.");

	/// <summary>Reply when the operation would act on the match's creator or a referee.</summary>
	public static readonly Reply TargetIsManager = new("mp.targetIsManager",
		"That can't be done to the match's creator or referees.");

	/// <summary>Reply when the target is not banned from this match.</summary>
	public static readonly Reply TargetNotBanned =
		new("mp.targetNotBanned", "That player is not banned from this match.");

	/// <summary>Reply when the target is not online.</summary>
	public static readonly Reply TargetNotOnline = new("mp.targetNotOnline", "That player is not online.");

	/// <summary>Reply when the invited player's settings do not allow the invitation.</summary>
	public static readonly Reply CannotInvite = new("mp.cannotInvite", "That player cannot be invited by you.");

	/// <summary>Reply when the match already holds the maximum number of referees.</summary>
	public static readonly Reply RefereeLimitReached = new("mp.refereeLimitReached",
		"This match already has the most referees it can have.");

	/// <summary>Reply when the target is already a referee of this match.</summary>
	public static readonly Reply TargetAlreadyReferee = new("mp.targetAlreadyReferee",
		"That user is already a referee of this match.");

	/// <summary>Reply when the target is not a referee of this match.</summary>
	public static readonly Reply TargetNotReferee =
		new("mp.targetNotReferee", "That user is not a referee of this match.");

	/// <summary>Reply when the target created this match.</summary>
	public static readonly Reply TargetIsCreator = new("mp.targetIsCreator", "That user created this match.");

	/// <summary>Reply when the target is playing in this match.</summary>
	public static readonly Reply TargetIsPlayer = new("mp.targetIsPlayer", "That user is playing in this match.");

	/// <summary>Reply when the target is not watching this match.</summary>
	public static readonly Reply TargetNotObserver =
		new("mp.targetNotObserver", "That user is not watching this match.");

	/// <summary>Reply when the actor already has the maximum number of tournament matches open.</summary>
	public static readonly Reply RoomLimitReached = new("mp.roomLimitReached",
		"You already have the most tournament matches open that you can.");

	/// <summary>Reply when no more matches can be opened right now.</summary>
	public static readonly Reply NoMatchesAvailable =
		new("mp.noMatchesAvailable", "No more matches can be opened right now.");

	/// <summary>Reply when the operation would act on the actor's own slot.</summary>
	public static readonly Reply OwnSlotLocked = new("mp.ownSlotLocked", "You can't lock your own slot.");

	/// <summary>Reply when the match has no teams.</summary>
	public static readonly Reply MatchHasNoTeams = new("mp.matchHasNoTeams", "This match has no teams.");

	/// <summary>Reply when FreeMod is not enabled.</summary>
	public static readonly Reply FreemodNotEnabled = new("mp.freemodNotEnabled", "FreeMod is not enabled.");

	/// <summary>Reply when a speed-changing mod was requested for a single slot.</summary>
	public static readonly Reply SpeedModsMatchWide = new("mp.speedModsMatchWide",
		"Speed-changing mods can only be set for the whole match.");

	/// <summary>Reply when the requested settings are not valid.</summary>
	public static readonly Reply SettingsInvalid = new("mp.settingsInvalid", "Those settings are not valid.");

	/// <summary>Reply when no beatmap is selected.</summary>
	public static readonly Reply NoBeatmapSelected = new("mp.noBeatmapSelected", "No beatmap is selected.");

	/// <summary>Reply when the target is not playing.</summary>
	public static readonly Reply TargetNotPlaying = new("mp.targetNotPlaying", "That player is not playing.");

	/// <summary>Reply when a countdown length is outside the allowed range.</summary>
	public static readonly Reply CountdownOutOfRange = new("mp.countdownOutOfRange",
		"The countdown must be between 1 second and 1 hour.");

	/// <summary>Reply when a score is not from the current round.</summary>
	public static readonly Reply ScoreNotCurrentRound =
		new("mp.scoreNotCurrentRound", "That score is not from the current round.");

	/// <summary>Reply when the match is closed.</summary>
	public static readonly Reply MatchClosed = new("mp.matchClosed", "The match is closed.");

	/// <summary>Maps a result to the user-visible text the bot sends.</summary>
	/// <param name="replies">Gives the text of the replies.</param>
	/// <param name="result">The result returned by the server for the requested room operation.</param>
	/// <returns>The text naming the missing permissions when there are any, otherwise the text for the outcome.</returns>
	public static string Describe(IReplies replies, Result<RoomOutcome> result)
	{
		return result.Missing is { } missing
			? replies.Text(MissingPermissions, new { permissions = missing.ToString() })
			: replies.Text(Of(result.Outcome), new { outcome = result.Outcome.ToString() });
	}

	/// <summary>Maps a <see cref="RoomOutcome" /> to the reply the bot sends.</summary>
	/// <param name="outcome">The outcome returned by the server for the requested room operation.</param>
	/// <returns>The reply for the outcome; <see cref="GenericFailed" /> when the outcome has no reply of its own.</returns>
	public static Reply Of(RoomOutcome outcome)
	{
		return outcome switch
		{
			RoomOutcome.RoleDenied => NotAuthorizedInMatch,
			RoomOutcome.AlreadySeated => AlreadyInThisMatch,
			RoomOutcome.Banned => BannedFromMatch,
			RoomOutcome.InAnotherRoom => TargetInAnotherMatch,
			RoomOutcome.WrongPassword => IncorrectPassword,
			RoomOutcome.Full => MatchIsFull,
			RoomOutcome.IsObserver => TargetIsObserver,
			RoomOutcome.NotInRoom => TargetNotInMatch,
			RoomOutcome.IsManager => TargetIsManager,
			RoomOutcome.NotBanned => TargetNotBanned,
			RoomOutcome.TargetOffline => TargetNotOnline,
			RoomOutcome.Blocked => CannotInvite,
			RoomOutcome.AlreadyInRoom => UserAlreadyInRoom,
			RoomOutcome.TooManyReferees => RefereeLimitReached,
			RoomOutcome.AlreadyReferee => TargetAlreadyReferee,
			RoomOutcome.NotReferee => TargetNotReferee,
			RoomOutcome.IsCreator => TargetIsCreator,
			RoomOutcome.IsPlayer => TargetIsPlayer,
			RoomOutcome.NotObserver => TargetNotObserver,
			RoomOutcome.TooManyRooms => RoomLimitReached,
			RoomOutcome.NoRoomId => NoMatchesAvailable,
			RoomOutcome.SlotNotOpen => DestinationSlotNotOpen,
			RoomOutcome.RoomLocked => MatchIsLocked,
			RoomOutcome.InProgress => MatchAlreadyInProgress,
			RoomOutcome.OwnSlot => OwnSlotLocked,
			RoomOutcome.NoTeams => MatchHasNoTeams,
			RoomOutcome.NotFreemod => FreemodNotEnabled,
			RoomOutcome.SpeedModNotAllowed => SpeedModsMatchWide,
			RoomOutcome.InvalidMods => InvalidMods,
			RoomOutcome.InvalidSettings => SettingsInvalid,
			RoomOutcome.NoBeatmap => NoBeatmapSelected,
			RoomOutcome.NotPlaying => TargetNotPlaying,
			RoomOutcome.NotInProgress => MatchNotInProgress,
			RoomOutcome.NoCountdown => NoCountdownRunning,
			RoomOutcome.RoundMismatch => ScoreNotCurrentRound,
			RoomOutcome.RoomClosed => MatchClosed,
			_ => GenericFailed
		};
	}
}