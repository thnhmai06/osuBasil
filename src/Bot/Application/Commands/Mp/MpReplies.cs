using Basil.Bot.Application.Basil;

namespace Basil.Bot.Application.Commands.Mp;

/// <summary>
///     The user-visible reply text sent by the <c>!mp</c> chat command surface.
/// </summary>
/// <remarks>
///     Wording is fixed in English here (the strings the bot posts to chat are public behavior, so
///     changing one is a contract change). Format strings use <c>{0}</c>/<c>{1}</c> placeholders
///     passed to <see cref="string.Format(string, object?[])" />.
/// </remarks>
internal static class MpReplies
{
	// ── room scope ─────────────────────────────────────────────────────────────────────────
	/// <summary>Names the target room on each reply line posted outside that room's own channel; <c>{0}</c> is the room id.</summary>
	public const string RoomPrefix = "[#{0}] ";

	// ── !mp make / makeprivate ─────────────────────────────────────────────────────────────
	/// <summary>Reply after a room is created; <c>{0}</c> is the room id, <c>{1}</c> its name, <c>{2}</c> a privacy suffix.</summary>
	public const string CreatedMatch =
		"Created the match #{0} {1}{2}. You're now targeting it, and have been added as a referee.";

	// ── !mp join ───────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp join</c>.</summary>
	public const string JoinUsage = "Usage: !mp join <id> [password]";

	/// <summary>Reply when no live room carries the requested id; <c>{0}</c> is the id.</summary>
	public const string NoActiveRoomWithId = "No active match with id {0}.";

	/// <summary>Reply when a private room rejects a non-invitee; <c>{0}</c> is the room id.</summary>
	public const string PrivateRoomJoinDenied =
		"Cannot join match #{0} — the room is private. Ask a referee for an invite.";

	/// <summary>Reply when the sender is banned from the room.</summary>
	public const string BannedFromMatch = "You're banned from this match.";

	/// <summary>Reply after joining a room; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string JoinedMatch = "Joined match #{0} {1}";

	/// <summary>Reply when the supplied password is wrong.</summary>
	public const string IncorrectPassword = "Incorrect password.";

	/// <summary>Reply when every slot is taken.</summary>
	public const string MatchIsFull = "The match is full.";

	/// <summary>Reply when the room is locked.</summary>
	public const string MatchIsLocked = "The match is locked.";

	/// <summary>Reply when the join failed for an otherwise-unreported reason.</summary>
	public const string FailedToJoinMatch = "Failed to join the match.";

	// ── !mp in ─────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when the sender has no stored scope and is not in any room.</summary>
	public const string NotScopedToAnyMatch = "You aren't targeting any match right now.";

	/// <summary>Reply when the stored scope points at a room that is no longer live; <c>{0}</c> is the id.</summary>
	public const string WasScopedToGoneMatch =
		"You were targeting match #{0}, but it's no longer live.";

	/// <summary>Reply reporting the current scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string CurrentlyScopedToMatch = "Currently targeting match #{0} {1}.";

	/// <summary>Usage line for <c>!mp in</c>.</summary>
	public const string InUsage = "Usage: !mp in [match_id]";

	/// <summary>Reply when the requested id names no live room; <c>{0}</c> is the id.</summary>
	public const string NoActiveRoomWithHashId = "No active match with id #{0}.";

	/// <summary>Reply after switching the scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string NowTargetingMatch = "Now targeting match #{0} {1}.";

	// ── !mp settings ──────────────────────────────────────────────────────────────────────
	/// <summary>First <c>!mp settings</c> line; <c>{0}</c> is the room name, <c>{1}</c> the id.</summary>
	public const string SettingsRoomName = "Room name: {0} (#{1})";

	/// <summary>Beatmap line in <c>!mp settings</c>; <c>{0}</c> is the map id, <c>{1}</c> its full name.</summary>
	public const string SettingsBeatmap = "Beatmap: {0} {1}";

	/// <summary>Beatmap line in <c>!mp settings</c> when no beatmap has been selected at all.</summary>
	public const string SettingsBeatmapNotSelected = "Beatmap: None selected";

	/// <summary>Second <c>!mp settings</c> line; <c>{0}</c> is the team type, <c>{1}</c> the win condition.</summary>
	public const string SettingsTeamMode = "Team mode: {0}, Win condition: {1}";

	/// <summary>Mods line in <c>!mp settings</c>; <c>{0}</c> is the comma-joined mod list.</summary>
	public const string SettingsActiveMods = "Active mods: {0}";

	/// <summary>Creator line in <c>!mp settings</c>; <c>{0}</c> is the user id, <c>{1}</c> the name.</summary>
	public const string SettingsCreator = "Creator: #{0} {1}";

	/// <summary>Player-count line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public const string SettingsPlayers = "Players: ({0})";

	// ── !mp lock / unlock / size ───────────────────────────────────────────────────────────
	/// <summary>Reply after locking the room.</summary>
	public const string LockedMatch = "Locked the match";

	/// <summary>Reply after unlocking the room.</summary>
	public const string UnlockedMatch = "Unlocked the match";

	/// <summary>Usage line for <c>!mp size</c>.</summary>
	public const string SizeUsage = "Usage: !mp size <1-16>";

	/// <summary>Reply after resizing the room; <c>{0}</c> is the new slot count.</summary>
	public const string ChangedMatchSize = "Changed match to size {0}";

	// ── !mp move / host / clearhost ────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp move</c>.</summary>
	public const string MoveUsage = "Usage: !mp move <name/id> <slot 1-16>";

	/// <summary>Reply when the target cannot be resolved to a seated player.</summary>
	public const string UserNotInMatchOrUnregistered = "User is not in this match or not registered.";

	/// <summary>Reply when the destination slot is occupied or otherwise not open.</summary>
	public const string DestinationSlotNotOpen = "Destination slot is not open.";

	/// <summary>Reply after moving a player; <c>{0}</c> is the player's name, <c>{1}</c> the destination slot.</summary>
	public const string MovedToSlot = "Moved {0} into slot {1}";

	/// <summary>Usage line for <c>!mp host</c>.</summary>
	public const string HostUsage = "Usage: !mp host <name/id>";

	/// <summary>Reply after transferring host; <c>{0}</c> is the new host's name.</summary>
	public const string ChangedMatchHost = "Changed match host to {0}";

	/// <summary>Reply after clearing the host.</summary>
	public const string ClearedMatchHost = "Cleared match host";

	// ── !mp name / password / private ──────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp name</c>.</summary>
	public const string NameUsage = "Usage: !mp name <text>";

	/// <summary>Reply after renaming the room; <c>{0}</c> is the new name.</summary>
	public const string RoomNameUpdated = "Room name updated to \"{0}\"";

	/// <summary>Reply after clearing the room password.</summary>
	public const string RemovedMatchPassword = "Removed the match password";

	/// <summary>Reply after changing the room password.</summary>
	public const string ChangedMatchPassword = "Changed the match password";

	/// <summary>Reply reporting the room's privacy; <c>{0}</c> is <c>private</c> or <c>not private</c>.</summary>
	public const string MatchIsPrivateNow = "This match is {0}.";

	/// <summary>Reply after making the room private.</summary>
	public const string MatchNowPrivate =
		"The match is now private. It will be hidden from the lobby and open only to invited players.";

	/// <summary>Reply after making the room public.</summary>
	public const string MatchNowPublic = "The match is now public.";

	/// <summary>Usage line for <c>!mp private</c>.</summary>
	public const string PrivateUsage = "Usage: !mp private [0|1]";

	// ── !mp invite ─────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp invite</c>.</summary>
	public const string InviteUsage = "Usage: !mp invite <name/id>";

	/// <summary>Reply when the target is not connected with the osu! client.</summary>
	public const string InviteRequiresClient = "User must be connected via the osu! client to be invited.";

	/// <summary>Reply when the target is already seated in the room.</summary>
	public const string UserAlreadyInRoom = "User is already in the room";

	/// <summary>Reply after inviting a player; <c>{0}</c> is the invitee's name.</summary>
	public const string InvitedToRoom = "Invited {0} to the room";

	// ── !mp addref / removeref / listrefs ──────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp addref</c>.</summary>
	public const string AddRefUsage = "Usage: !mp addref <name/id>";

	/// <summary>Reply when the target cannot be resolved to any user.</summary>
	public const string UserNotFound = "User not found";

	/// <summary>Reply after adding a referee; <c>{0}</c> is the new referee's name.</summary>
	public const string AddedReferee = "Added {0} to the match referees";

	/// <summary>Usage line for <c>!mp removeref</c>.</summary>
	public const string RemoveRefUsage = "Usage: !mp removeref <name/id>";

	/// <summary>Reply after removing a referee; <c>{0}</c> is the removed referee's name.</summary>
	public const string RemovedReferee = "Removed {0} from the match referees";

	/// <summary>Reply when the match has no referees.</summary>
	public const string NoReferees = "No referees";

	/// <summary>Header line before the list of referees.</summary>
	public const string MatchReferees = "Match referees:";

	/// <summary>Reply when the match has no banned players.</summary>
	public const string NoBannedPlayers = "No banned players";

	/// <summary>Header line before the list of banned players.</summary>
	public const string MatchBans = "Match bans:";

	// ── !mp team / set ─────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp team</c>.</summary>
	public const string TeamUsage = "Usage: !mp team <name/id> <red|blue>";

	/// <summary>Reply after assigning a team; <c>{0}</c> is the player's name, <c>{1}</c> the team label.</summary>
	public const string MovedToTeam = "Moved {0} to team {1}";

	/// <summary>Usage line for <c>!mp set</c>.</summary>
	public const string SetUsage = "Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]";

	/// <summary>Reply after changing settings; <c>{0}</c> is the team type, <c>{1}</c> the win condition, <c>{2}</c> a size suffix.</summary>
	public const string ChangedMatchSettings = "Changed match settings to {0}, {1}{2}";

	// ── !mp map / mods ─────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp map</c>.</summary>
	public const string MapUsage = "Usage: !mp map <beatmap id> [playmode]";

	/// <summary>Reply when no beatmap carries the requested id; <c>{0}</c> is the id.</summary>
	public const string NoBeatmapWithId = "No beatmap with ID {0} found.";

	/// <summary>Reply after changing the map; <c>{0}</c> artist, <c>{1}</c> title, <c>{2}</c> version.</summary>
	public const string ChangedBeatmap = "Changed beatmap to {0} - {1} [{2}]";

	/// <summary>Reply when the requested mod combination is invalid for the room's mode.</summary>
	public const string InvalidMods = "Invalid mods.";

	/// <summary>Usage line for <c>!mp mods</c>.</summary>
	public const string ModsUsage = "Usage: !mp mods <mods>|Freemod|None";

	/// <summary>Mod-change summary, enabled case; <c>{0}</c> is the mod list.</summary>
	public const string EnabledMods = "Enabled {0}";

	/// <summary>Mod-change summary, disabled case; <c>{0}</c> is the mod list.</summary>
	public const string DisabledMods = "Disabled {0}";

	/// <summary>Mod-change summary line when freemod was turned off.</summary>
	public const string DisabledFreemod = "Disabled FreeMod";

	/// <summary>Mod-change summary line when freemod was turned on.</summary>
	public const string EnabledFreemod = "Enabled FreeMod";

	/// <summary>Mod-change summary when nothing changed.</summary>
	public const string NoModChanges = "No mod changes";

	// ── !mp start / timer / aborttimer / abort ─────────────────────────────────────────────
	/// <summary>Reply when the match is already in progress.</summary>
	public const string MatchAlreadyInProgress = "Match is already in progress.";

	/// <summary>Reply when a start countdown was queued; <c>{0}</c> is the delay in seconds.</summary>
	public const string MatchStartsInSeconds = "Match starts in {0} seconds";

	/// <summary>Reply after starting the match immediately.</summary>
	public const string MatchStarted = "Match started";

	/// <summary>Usage line for <c>!mp timer</c>.</summary>
	public const string TimerUsage = "Usage: !mp timer [seconds]";

	/// <summary>Reply after starting a countdown; <c>{0}</c> is the duration in seconds.</summary>
	public const string CountdownStarted = "Countdown started: {0} seconds";

	/// <summary>Reply when no countdown is running.</summary>
	public const string NoCountdownRunning = "No countdown is running.";

	/// <summary>Reply after aborting a countdown.</summary>
	public const string CountdownAborted = "Countdown aborted";

	/// <summary>Reply when the match is not in progress.</summary>
	public const string MatchNotInProgress = "Match is not in progress.";

	/// <summary>Reply after aborting the match.</summary>
	public const string AbortedMatch = "Aborted the match";

	// ── !mp kick / ban / unban / close ─────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp kick</c>.</summary>
	public const string KickUsage = "Usage: !mp kick <name/id>";

	/// <summary>Reply after kicking a player; <c>{0}</c> is the kicked player's name.</summary>
	public const string KickedFromMatch = "Kicked {0} from the match";

	/// <summary>Usage line for <c>!mp ban</c>.</summary>
	public const string BanUsage = "Usage: !mp ban <name/id>";

	/// <summary>Reply when the target is not a registered user.</summary>
	public const string UserNotRegistered = "User is not registered.";

	/// <summary>Reply after banning a player; <c>{0}</c> is the banned player's name.</summary>
	public const string BannedPlayerFromMatch = "Banned {0} from the match";

	/// <summary>Usage line for <c>!mp unban</c>.</summary>
	public const string UnbanUsage = "Usage: !mp unban <name/id>";

	/// <summary>Reply after unbanning a player; <c>{0}</c> is the unbanned player's name.</summary>
	public const string UnbannedFromMatch = "Unbanned {0} from the match";

	/// <summary>Reply after closing the match.</summary>
	public const string ClosedMatch = "Closed the match";

	// ── dispatch errors ────────────────────────────────────────────────────────────────────
	/// <summary>Reply when a subcommand name is not recognized; <c>{0}</c> is the subcommand.</summary>
	public const string UnknownMpSubcommand =
		"Unknown !mp subcommand: {0}. Use !mp help to list available subcommands.";

	/// <summary>Reply when a subcommand is limited to the room's creator; <c>{0}</c> is the subcommand.</summary>
	public const string CreatorOnlyMp = "Only the match's creator can run {0}.";

	/// <summary>Reply when <c>!mp in</c> is run from a channel instead of a DM to the bot.</summary>
	public const string MpInDmOnly = "!mp in only works in a DM to BasilBot.";

	/// <summary>Reply when the sender is not in any room and tried a non-command-scoped subcommand.</summary>
	public const string NotInARoom = "You are not in a match.";

	/// <summary>Generic fallback reply when no specific outcome mapping exists; <c>{0}</c> is the outcome name.</summary>
	public const string GenericFailed = "Couldn't do that ({0}).";

	// ── help text ─────────────────────────────────────────────────────────────────────────
	/// <summary>The <c>!mp help</c> listing, one subcommand per line.</summary>
	public const string HelpText =
		"!mp settings - show match id, map, team type, win condition, mods, and slots" + "\n" +
		"!mp lock - lock the room, blocking new joins" + "\n" +
		"!mp unlock - unlock the room" + "\n" +
		"!mp private [0|1] - show or set the room's private status (hidden from lobby, invite-only)" + "\n" +
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
		"!mp close - close the match immediately";

	// ── outcome replies ───────────────────────────────────────────────────────────────────
	/// <summary>Reply when the actor may not perform the requested operation in this match.</summary>
	public const string NotAuthorizedInMatch = "You don't have permission to do that in this match.";

	/// <summary>Reply when the actor is silenced and may not perform the operation.</summary>
	public const string Silenced = "You can't do that while you are silenced.";

	/// <summary>Reply when the actor is already seated in this match.</summary>
	public const string AlreadyInThisMatch = "You're already in this match.";

	/// <summary>Reply when the target is seated in a different match.</summary>
	public const string TargetInAnotherMatch = "That player is in another match.";

	/// <summary>Reply when the target is watching this match as an observer.</summary>
	public const string TargetIsObserver = "That player is watching this match as an observer.";

	/// <summary>Reply when the target is not in this match.</summary>
	public const string TargetNotInMatch = "That player is not in this match.";

	/// <summary>Reply when the operation would act on the match's creator or a referee.</summary>
	public const string TargetIsManager = "That can't be done to the match's creator or referees.";

	/// <summary>Reply when the target is not banned from this match.</summary>
	public const string TargetNotBanned = "That player is not banned from this match.";

	/// <summary>Reply when the target is not online.</summary>
	public const string TargetNotOnline = "That player is not online.";

	/// <summary>Reply when the match already holds the maximum number of referees.</summary>
	public const string RefereeLimitReached = "This match already has the most referees it can have.";

	/// <summary>Reply when the target is already a referee of this match.</summary>
	public const string TargetAlreadyReferee = "That user is already a referee of this match.";

	/// <summary>Reply when the target is not a referee of this match.</summary>
	public const string TargetNotReferee = "That user is not a referee of this match.";

	/// <summary>Reply when the target created this match.</summary>
	public const string TargetIsCreator = "That user created this match.";

	/// <summary>Reply when the target is playing in this match.</summary>
	public const string TargetIsPlayer = "That user is playing in this match.";

	/// <summary>Reply when the target is not watching this match.</summary>
	public const string TargetNotObserver = "That user is not watching this match.";

	/// <summary>Reply when the actor already has the maximum number of tournament matches open.</summary>
	public const string RoomLimitReached = "You already have the most tournament matches open that you can.";

	/// <summary>Reply when no more matches can be opened right now.</summary>
	public const string NoMatchesAvailable = "No more matches can be opened right now.";

	/// <summary>Reply when the operation would act on the actor's own slot.</summary>
	public const string OwnSlotLocked = "You can't lock your own slot.";

	/// <summary>Reply when the match has no teams.</summary>
	public const string MatchHasNoTeams = "This match has no teams.";

	/// <summary>Reply when FreeMod is not enabled.</summary>
	public const string FreemodNotEnabled = "FreeMod is not enabled.";

	/// <summary>Reply when a speed-changing mod was requested for a single slot.</summary>
	public const string SpeedModsMatchWide = "Speed-changing mods can only be set for the whole match.";

	/// <summary>Reply when the requested settings are not valid.</summary>
	public const string SettingsInvalid = "Those settings are not valid.";

	/// <summary>Reply when no beatmap is selected.</summary>
	public const string NoBeatmapSelected = "No beatmap is selected.";

	/// <summary>Reply when the target is not playing.</summary>
	public const string TargetNotPlaying = "That player is not playing.";

	/// <summary>Reply when a countdown length is outside the allowed range.</summary>
	public const string CountdownOutOfRange = "The countdown must be between 1 second and 1 hour.";

	/// <summary>Reply when a score is not from the current round.</summary>
	public const string ScoreNotCurrentRound = "That score is not from the current round.";

	/// <summary>Reply when the match is closed.</summary>
	public const string MatchClosed = "The match is closed.";

	/// <summary>Reply when the requested room, user or beatmap does not exist.</summary>
	public const string NotFound = "Not found.";

	/// <summary>Maps a <see cref="RoomOutcome" /> to the user-visible reply the bot sends.</summary>
	/// <param name="outcome">The outcome returned by the server for the requested room operation.</param>
	/// <returns>The reply text, with any placeholders already filled in for constant replies.</returns>
	public static string Describe(RoomOutcome outcome)
	{
		return outcome switch
		{
			RoomOutcome.NotAuthorized => NotAuthorizedInMatch,
			RoomOutcome.Silenced => Silenced,
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
			RoomOutcome.OutOfRange => CountdownOutOfRange,
			RoomOutcome.NoCountdown => NoCountdownRunning,
			RoomOutcome.RoundMismatch => ScoreNotCurrentRound,
			RoomOutcome.RoomClosed => MatchClosed,
			RoomOutcome.NotFound => NotFound,
			_ => string.Format(GenericFailed, outcome)
		};
	}
}