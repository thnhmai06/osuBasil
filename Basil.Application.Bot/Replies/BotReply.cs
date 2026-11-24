using Basil.Application.Shared.Replies;

namespace Basil.Application.Bot.Replies;

/// <summary>Every reply BasilBot says, each with its default wording; the operator can reword them in <c>Data/Localizations/bot.json</c>.</summary>
internal enum BotReply
{
	// ── Room countdown ─────────────────────────────────────────────────────────────────────
	[Reply("Queued the round to start in {seconds} seconds.", "seconds")]
	AnnounceCountdownStartedForRound,

	[Reply("Started a {seconds}-second countdown.", "seconds")]
	AnnounceCountdownStarted,

	[Reply("The round starts in {seconds} seconds.", "seconds")]
	AnnounceCountdownTickedForRound,

	[Reply("{seconds} seconds remaining.", "seconds")]
	AnnounceCountdownTicked,

	[Reply("The round cannot start because no beatmap has been selected.")]
	AnnounceCountdownElapsedForRound,

	[Reply("Countdown finished.")]
	AnnounceCountdownElapsed,

	[Reply("Countdown aborted.")]
	AnnounceCountdownCancelled,

	[Reply("Countdown cancelled with {seconds} seconds remaining. A new countdown is starting.", "seconds")]
	AnnounceCountdownReplaced,

	[Reply("Countdown cancelled because the round was started.")]
	AnnounceCountdownCancelledByRoundStart,

	[Reply("Countdown cancelled because the round was aborted.")]
	AnnounceCountdownCancelledByRoundAbort,


	// ── Room round ─────────────────────────────────────────────────────────────────────────
	[Reply("Good luck, have fun!")]
	AnnounceRoundStartedByCountdown,

	[Reply("The round cannot start because the room has no players.")]
	AnnounceRoundStartedNoPlayers,

	[Reply("The round was aborted.")]
	AnnounceRoundAborted,

	[Reply("Round #{id} has ended. Please wait {seconds} seconds while the final results arrive.", "id", "seconds")]
	AnnounceRoundEnded,

	[Reply("All scores have been received.")]
	AnnounceScoresReceived,

	[Reply("All scores have been received except from:")]
	AnnounceScoresReceivedExcept,

	[Reply("{player}", "player")]
	AnnounceScoreMissing,


	// ── Room settings ──────────────────────────────────────────────────────────────────────
	[Reply("Round start cancelled because the room settings changed.")]
	AnnounceSettingsChangedCountdownCancelled,


	// ── Room closing ───────────────────────────────────────────────────────────────────────
	[Reply("This room is empty and will close in {minutes} minutes unless a player joins.", "minutes")]
	AnnounceRoomClosingAnnounced,

	[Reply("A player joined. The room will no longer close.")]
	AnnounceRoomPlayerJoinedClosing,


	// ── Anticheat ──────────────────────────────────────────────────────────────────────────
	[Reply("Anti-cheat flag for {player}: {reason}", "player", "reason")]
	AnnounceAnticheatFlagRoom,

	[Reply("Anti-cheat flag in room #{room} {name} for {player}: {reason}", "room", "name", "player", "reason")]
	AnnounceAnticheatFlagPm,


	// ── !where ───────────────────────────────────────────────────────────────────────────────
	[Reply("Usage: !where <username>")]
	BotWhereUsage,

	[Reply("The country of {name} is not known.", "name")]
	BotWhereUnknownCountry,

	[Reply("Unknown command {command}. Type !help to see the commands.", "command")]
	BotUnknownCommand,

	[Reply("yes")]
	BotYes,

	[Reply("no")]
	BotNo,

	[Reply("{name} is in {country}.", "name", "country")]
	BotWhereIsIn,


	// ── !faq ─────────────────────────────────────────────────────────────────────────────────
	[Reply("Usage: !faq <entry>|list")]
	BotFaqUsage,

	[Reply("No FAQ entry found for ''{entry}''.", "entry")]
	BotNoFaqEntryFound,

	[Reply("No FAQ entries available.")]
	BotNoFaqEntriesAvailable,

	[Reply("Available FAQ entries: {entries}", "entries")]
	BotAvailableFaqEntries,


	// ── !roll ────────────────────────────────────────────────────────────────────────────────
	[Reply("{user} rolls {points} point(s)", "user", "points")]
	BotRollResult,

	[Reply("Usage: !roll [max], where max is a positive whole number.")]
	BotRollUsage,


	// ── Chain errors ─────────────────────────────────────────────────────────────────────────
	[Reply("Chains are only allowed for !mp commands.")]
	BotChainOnlyMpAllowed,

	[Reply("Chains are not allowed in #lobby.")]
	BotChainNotInLobby,

	[Reply("Usage: !channel list | create <#name> [topic=<text>] [read=<mask>] [write=<mask>] [autojoin=<true|false>] [visible=<true|false>] | set <#name> <option>=<value>... | delete <#name>")]
	ChannelUsage,

	[Reply("{name} read={read} write={write} autojoin={autoJoin} visible={visible} topic={topic}", "name", "read", "write", "autoJoin", "visible", "topic")]
	ChannelListEntry,

	[Reply("There are no channels you can see.")]
	ChannelNoChannels,

	[Reply("Created channel {name}.", "name")]
	ChannelCreated,

	[Reply("Updated channel {name}.", "name")]
	ChannelUpdated,

	[Reply("Deleted channel {name}.", "name")]
	ChannelDeleted,

	[Reply("A channel named {name} already exists.", "name")]
	ChannelNameTaken,

	[Reply("There is no channel named {name}.", "name")]
	ChannelNotFound,

	[Reply("That is not a valid channel name or option. Type !channel for the usage.")]
	ChannelInvalidArguments,

	[Reply("Only a moderator can manage channels.")]
	ChannelMissingPrivileges,

	[Reply("The server refused that: {outcome}.", "outcome")]
	ChannelRefused,


	// ── room scope ─────────────────────────────────────────────────────────────────────────
	[Reply("[#{room}] ", "room")]
	MpRoomPrefix,


	// ── !mp make / makeprivate ─────────────────────────────────────────────────────────────
	[Reply("Created room #{room} {name}{privacy, select, private { (private)} other {}}. You are now targeting it. Join {channel} to chat in it.", "room", "name", "privacy", "channel")]
	MpCreatedMatch,

	[Reply("{name}'s room", "name")]
	MpDefaultRoomName,


	// ── !mp join ───────────────────────────────────────────────────────────────────────────
	[Reply("Usage: !mp join <id> [password]")]
	MpJoinUsage,

	[Reply("No active room with id {room}.", "room")]
	MpNoActiveRoomWithId,

	[Reply("You are banned from this room.")]
	MpBannedFromMatch,

	[Reply("Joined room #{room} {name}.", "room", "name")]
	MpJoinedMatch,

	[Reply("Incorrect password.")]
	MpIncorrectPassword,

	[Reply("The room is full.")]
	MpMatchIsFull,

	[Reply("The room is locked.")]
	MpMatchIsLocked,

	[Reply("Rooms are locked right now.")]
	MpRoomsLocked,


	// ── !mp in ─────────────────────────────────────────────────────────────────────────────
	[Reply("You are not targeting any room right now.")]
	MpNotScopedToAnyMatch,

	[Reply("Usage: !mp in [room_id]")]
	MpInUsage,

	[Reply("No active room with id #{room}.", "room")]
	MpNoActiveRoomWithHashId,

	[Reply("Now targeting room #{room} {name}.", "room", "name")]
	MpNowTargetingMatch,


	// ── !mp settings ──────────────────────────────────────────────────────────────────────
	[Reply("Room name: {name} (#{room})", "name", "room")]
	MpSettingsRoomName,

	[Reply("Beatmap: {beatmap} {name}", "beatmap", "name")]
	MpSettingsBeatmap,

	[Reply("Beatmap: None selected")]
	MpSettingsBeatmapNotSelected,

	[Reply("Team mode: {teamType}, Win condition: {winCondition}", "teamType", "winCondition")]
	MpSettingsTeamMode,

	[Reply("Active mods: {mods}", "mods")]
	MpSettingsActiveMods,

	[Reply("Creator: #{id} {name}", "id", "name")]
	MpSettingsCreator,

	[Reply("Size: {size} slots", "size")]
	MpSettingsSize,

	[Reply("Host: {host}", "host")]
	MpSettingsHost,

	[Reply("Referees: {referees}", "referees")]
	MpSettingsReferees,

	[Reply("Locked: {locked}, Private: {private}, Password: {password}", "locked", "private", "password")]
	MpSettingsAccess,

	[Reply("none")]
	MpNone,

	[Reply("Players: ({count})", "count")]
	MpSettingsPlayers,

	[Reply("Slot {slot}  {status} {team}{id} {name}{tags}", "slot", "status", "team", "id", "name", "tags")]
	MpSettingsSlot,

	[Reply("Not Ready")]
	MpSlotStatusNotReady,

	[Reply("No Map")]
	MpSlotStatusNoMap,

	[Reply("Host")]
	MpHostTag,

	[Reply("Freemod")]
	MpFreemod,

	[Reply(" [{tags}]", "tags")]
	MpTags,


	// ── !mp lock / unlock / size ───────────────────────────────────────────────────────────
	[Reply("Locked the room.")]
	MpLockedMatch,

	[Reply("Unlocked the room.")]
	MpUnlockedMatch,

	[Reply("Usage: !mp size <2-16>")]
	MpSizeUsage,

	[Reply("The room size must be between {min} and {max}.", "min", "max")]
	MpSizeOutOfRange,

	[Reply("Commands: {commands}. Type !mp help <command> for its usage.", "commands")]
	MpHelpList,



	[Reply("Changed the room size to {size}.", "size")]
	MpChangedMatchSize,


	// ── !mp move / host / clearhost ────────────────────────────────────────────────────────
	[Reply("Usage: !mp move <name/id> <slot 1-16>")]
	MpMoveUsage,

	[Reply("Destination slot is not open.")]
	MpDestinationSlotNotOpen,

	[Reply("Moved {player} into slot {slot}.", "player", "slot")]
	MpMovedToSlot,

	[Reply("Usage: !mp host <name/id>")]
	MpHostUsage,

	[Reply("Changed the room host to {player}.", "player")]
	MpChangedMatchHost,

	[Reply("Cleared the room host.")]
	MpClearedMatchHost,


	// ── !mp name / password / private ──────────────────────────────────────────────────────
	[Reply("Usage: !mp name <text>")]
	MpNameUsage,

	[Reply("Room name updated to \"{name}\".", "name")]
	MpRoomNameUpdated,

	[Reply("Removed the room password.")]
	MpRemovedMatchPassword,

	[Reply("Changed the room password.")]
	MpChangedMatchPassword,

	[Reply("This room is {privacy, select, private {private} other {not private}}.", "privacy")]
	MpMatchIsPrivateNow,

	[Reply("The room is now private. It is hidden from the lobby, and only players who took part in it, its referees and server managers can view its history.")]
	MpMatchNowPrivate,

	[Reply("The room is now public.")]
	MpMatchNowPublic,

	[Reply("Usage: !mp private [0|1]")]
	MpPrivateUsage,


	// ── !mp invite ─────────────────────────────────────────────────────────────────────────
	[Reply("Usage: !mp invite <name/id>")]
	MpInviteUsage,

	[Reply("User is already in a room.")]
	MpUserAlreadyInRoom,

	[Reply("Invited {player} to the room.", "player")]
	MpInvitedToRoom,


	// ── !mp addref / removeref / listrefs ──────────────────────────────────────────────────
	[Reply("Usage: !mp addref <name/id>")]
	MpAddRefUsage,

	[Reply("That user was not found.")]
	MpUserNotFound,

	[Reply("Added {player} to the room referees.", "player")]
	MpAddedReferee,

	[Reply("Usage: !mp removeref <name/id>")]
	MpRemoveRefUsage,

	[Reply("Removed {player} from the room referees.", "player")]
	MpRemovedReferee,

	[Reply("No referees.")]
	MpNoReferees,

	[Reply("Room referees:")]
	MpMatchReferees,

	[Reply("No banned players.")]
	MpNoBannedPlayers,

	[Reply("Room bans:")]
	MpMatchBans,


	// ── !mp team / set ─────────────────────────────────────────────────────────────────────
	[Reply("Usage: !mp team <name/id> <red|blue>")]
	MpTeamUsage,

	[Reply("Moved {player} to team {team}.", "player", "team")]
	MpMovedToTeam,

	[Reply("Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 2-16]")]
	MpSetUsage,

	[Reply("Changed the room settings to {teamType}, {winCondition}{size, select, none {.} other {, {size} slots.}}", "teamType", "winCondition", "size")]
	MpChangedMatchSettings,


	// ── !mp map / mods ─────────────────────────────────────────────────────────────────────
	[Reply("Usage: !mp map <beatmap id> [playmode]")]
	MpMapUsage,

	[Reply("No beatmap with ID {beatmap} found.", "beatmap")]
	MpNoBeatmapWithId,

	[Reply("Changed beatmap to {artist} - {title} [{version}].", "artist", "title", "version")]
	MpChangedBeatmap,

	[Reply("Invalid mods.")]
	MpInvalidMods,

	[Reply("Usage: !mp mods <mods>|Freemod|None")]
	MpModsUsage,

	[Reply("Enabled {mods}", "mods")]
	MpEnabledMods,

	[Reply("Disabled {mods}", "mods")]
	MpDisabledMods,

	[Reply("Disabled Freemod")]
	MpDisabledFreemod,

	[Reply("Enabled Freemod")]
	MpEnabledFreemod,

	[Reply("No mod changes.")]
	MpNoModChanges,


	// ── !mp start / timer / aborttimer / abort ─────────────────────────────────────────────
	[Reply("A round is already in progress.")]
	MpMatchAlreadyInProgress,

	[Reply("Started the round.")]
	MpMatchStarted,

	[Reply("Usage: !mp timer [seconds]")]
	MpTimerUsage,

	[Reply("No countdown is running.")]
	MpNoCountdownRunning,

	[Reply("No round is in progress.")]
	MpMatchNotInProgress,

	[Reply("Aborted the round.")]
	MpAbortedMatch,


	// ── !mp kick / ban / unban / close ─────────────────────────────────────────────────────
	[Reply("Usage: !mp kick <name/id>")]
	MpKickUsage,

	[Reply("Kicked {player} from the room.", "player")]
	MpKickedFromMatch,

	[Reply("Usage: !mp ban <name/id>")]
	MpBanUsage,

	[Reply("Banned {player} from the room.", "player")]
	MpBannedPlayerFromMatch,

	[Reply("Usage: !mp unban <name/id>")]
	MpUnbanUsage,

	[Reply("Unbanned {player} from the room.", "player")]
	MpUnbannedFromMatch,

	[Reply("Closed the room.")]
	MpClosedMatch,


	// ── dispatch errors ────────────────────────────────────────────────────────────────────
	[Reply("Unknown !mp subcommand: {subcommand}. Use !mp help to list available subcommands.", "subcommand")]
	MpUnknownMpSubcommand,

	[Reply("!mp in only works in a private message to BasilBot.")]
	MpMpInDmOnly,

	[Reply("You are not in a room. Join one, or target one with !mp in <id> in a private message.")]
	MpNotInARoom,

	[Reply("That could not be done ({outcome}).", "outcome")]
	MpGenericFailed,


	// ── outcome replies ───────────────────────────────────────────────────────────────────
	[Reply("Only the creator or a referee of the room can do that.")]
	MpMissingPrivileges,

	[Reply("Only a player can do that.")]
	MpPlayersOnly,

	[Reply("You are already in this room.")]
	MpAlreadyInThisMatch,

	[Reply("That player is in another room.")]
	MpTargetInAnotherMatch,

	[Reply("That player is watching this room as an observer.")]
	MpTargetIsObserver,

	[Reply("That player is not in this room.")]
	MpTargetNotInMatch,

	[Reply("That cannot be done to the room's creator or referees.")]
	MpTargetIsManager,

	[Reply("That player is not banned from this room.")]
	MpTargetNotBanned,

	[Reply("That player is banned from this room. Unban them first.")]
	MpTargetIsBanned,

	[Reply("That player is not online.")]
	MpTargetNotOnline,

	[Reply("That player cannot be invited by you.")]
	MpCannotInvite,

	[Reply("That user is already a referee of this room.")]
	MpTargetAlreadyReferee,

	[Reply("That user is not a referee of this room.")]
	MpTargetNotReferee,

	[Reply("That user created this room.")]
	MpTargetIsCreator,

	[Reply("That user is playing in this room.")]
	MpTargetIsPlayer,

	[Reply("That user is not watching this room.")]
	MpTargetNotObserver,

	[Reply("You already have the most rooms open that you can.")]
	MpRoomLimitReached,

	[Reply("No more rooms can be opened right now.")]
	MpNoMatchesAvailable,

	[Reply("You cannot lock your own slot.")]
	MpOwnSlotLocked,

	[Reply("This room has no teams.")]
	MpMatchHasNoTeams,

	[Reply("Freemod is not enabled.")]
	MpFreemodNotEnabled,

	[Reply("Speed-changing mods can only be set for the whole match.")]
	MpSpeedModsMatchWide,

	[Reply("Tag team types are only available in osu!.")]
	MpTagNotAvailable,

	[Reply("Mods cannot be changed while the player is ready.")]
	MpPlayerReady,

	[Reply("Those settings are not valid.")]
	MpSettingsInvalid,

	[Reply("No beatmap is selected.")]
	MpNoBeatmapSelected,

	[Reply("That player is not playing.")]
	MpTargetNotPlaying,

	[Reply("The countdown must be between 1 second and 1 hour.")]
	MpCountdownOutOfRange,

	[Reply("That score is not from the current round.")]
	MpScoreNotCurrentRound,

	[Reply("The room is closed.")]
	MpMatchClosed,


	// ── Command help
	[Reply("list the commands")]
	BotHelpHelp,

	[Reply("roll a random number from 0 to max (default 100)")]
	BotHelpRoll,

	[Reply("show a user's country")]
	BotHelpWhere,

	[Reply("print a FAQ entry, or list every entry")]
	BotHelpFaq,

	[Reply("list multiplayer subcommands")]
	BotHelpMp,

	[Reply("manage the general channels (needs privilege)")]
	BotHelpChannel,

	[Reply("create a public room and become its creator")]
	MpHelpMake,

	[Reply("create a private room and become its creator")]
	MpHelpMakeprivate,

	[Reply("join a room")]
	MpHelpJoin,

	[Reply("target a room from a private message; omit the id to clear the target")]
	MpHelpIn,

	[Reply("show the room's id, name, map, team type, win condition, mods, size, host, referees, lock, privacy and slots")]
	MpHelpSettings,

	[Reply("lock the slots, so players cannot change slot or team")]
	MpHelpLock,

	[Reply("unlock the room")]
	MpHelpUnlock,

	[Reply("show or set the room's private status (hidden from the lobby and the room lists)")]
	MpHelpPrivate,

	[Reply("set the number of available slots")]
	MpHelpSize,

	[Reply("move a user to another slot")]
	MpHelpMove,

	[Reply("transfer host to another user")]
	MpHelpHost,

	[Reply("clear the current host")]
	MpHelpClearhost,

	[Reply("rename the room")]
	MpHelpName,

	[Reply("set the room password; omit to clear it")]
	MpHelpPassword,

	[Reply("invite an online user")]
	MpHelpInvite,

	[Reply("add a referee (creator only)")]
	MpHelpAddref,

	[Reply("remove a referee (creator only)")]
	MpHelpRemoveref,

	[Reply("list current referees")]
	MpHelpListrefs,

	[Reply("list players banned from this room")]
	MpHelpBanlist,

	[Reply("assign a user's team")]
	MpHelpTeam,

	[Reply("change the selected map")]
	MpHelpMap,

	[Reply("set the room mods")]
	MpHelpMods,

	[Reply("set team type, win condition, and size at once")]
	MpHelpSet,

	[Reply("start now, or after a countdown")]
	MpHelpStart,

	[Reply("start a countdown without auto-starting")]
	MpHelpTimer,

	[Reply("cancel a running countdown")]
	MpHelpAborttimer,

	[Reply("abort the round in progress")]
	MpHelpAbort,

	[Reply("remove a user from the room")]
	MpHelpKick,

	[Reply("kick and block a user from rejoining")]
	MpHelpBan,

	[Reply("allow a banned user to rejoin")]
	MpHelpUnban,

	[Reply("close the room immediately")]
	MpHelpClose,
}
