using Basil.Application.Abstractions.Bot;

namespace Basil.Application.Services.Bot;

/// <summary>
///     The user-visible reply text sent by the BasilBot chat commands, the single source of truth
///     for that surface.
/// </summary>
/// <remarks>
///     Every reply the bot sends through an <see cref="ICommandReplySink" /> — <c>!mp</c> and
///     <c>!where</c>/<c>!faq</c>/<c>!roll</c>/<c>!help</c> — is a named constant here. Production
///     services emit these constants and tests assert against the same symbols, so the two cannot
///     drift. Format strings use <see cref="string.Format(string, object?[])" /> placeholders;
///     fixed strings are plain <see cref="string" /> constants. Deliberately changing the wording of
///     a reply is a public-behavior change: a one-line edit to the constant here, and every test
///     that pins it stays in sync.
/// </remarks>
public static class MpReplies
{
	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────────
	/// <summary>Reply when a room could not be created.</summary>
	public const string CreateFailed = "Couldn't create the match — please try again.";

	/// <summary>Reply after a room is created; <c>{0}</c> is the room id, <c>{1}</c> its name, <c>{2}</c> a privacy suffix.</summary>
	public const string CreatedMatch =
		"Created the match #{0} {1}{2}. You are now scoped to this match, and added as a referee.";

	// ── !mp join ─────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp join</c>.</summary>
	public const string JoinUsage = "Usage: !mp join <id> [password]";

	/// <summary>Reply when no live match carries the requested id; <c>{0}</c> is the id.</summary>
	public const string NoActiveMatchWithId = "No active match with id {0}.";

	/// <summary>Reply when a private room rejects a non-invitee; <c>{0}</c> is the room id.</summary>
	public const string PrivateRoomJoinDenied =
		"Cannot join match #{0} — the room is private. Ask a referee for an invite.";

	/// <summary>Reply when the sender is already seated in another match.</summary>
	public const string AlreadyInAMatch = "You're already in a match.";

	/// <summary>Reply when the sender is banned from the match.</summary>
	public const string BannedFromMatch = "You're banned from this match.";

	/// <summary>Reply after joining a match's slots; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string JoinedMatch = "Joined match #{0} {1}";

	/// <summary>Reply when the supplied password is wrong.</summary>
	public const string IncorrectPassword = "Incorrect password.";

	/// <summary>Reply when every slot is taken.</summary>
	public const string MatchIsFull = "The match is full.";

	/// <summary>Reply when the room is locked.</summary>
	public const string MatchIsLocked = "The match is locked.";

	/// <summary>Reply when the join failed for an otherwise-unreported reason.</summary>
	public const string FailedToJoinMatch = "Failed to join the match.";

	/// <summary>Reply when an IRC session could not join the match's chat channel.</summary>
	public const string FailedToJoinMatchChat = "Failed to join the match chat.";

	/// <summary>Reply after an IRC session joins a match's chat; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string JoinedMatchChat = "Joined match #{0} {1}'s chat.";

	// ── !mp in ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when the sender has no stored scope and is not in any match.</summary>
	public const string NotScopedToAnyMatch = "You're not scoped to any match.";

	/// <summary>
	///     Reply when the stored scope points at a match that is no longer live; <c>{0}</c> is the id.
	/// </summary>
	/// <remarks>
	///     Says "no longer live", not "no longer exists": the match's row still exists (it can be
	///     read back via <c>GET /matches/{id}</c>), it's just no longer in the in-memory registry
	///     this lookup checks.
	/// </remarks>
	public const string WasScopedToGoneMatch = "You were scoped to match #{0}, but it is no longer live.";

	/// <summary>Reply reporting the current scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string CurrentlyScopedToMatch = "Currently scoped to match #{0} {1}.";

	/// <summary>Header line before the list of matches the sender referees.</summary>
	public const string YouAreARefereeOf = "You're a referee of:";

	/// <summary>Usage line for <c>!mp in</c>.</summary>
	public const string InUsage = "Usage: !mp in [match_id]";

	/// <summary>Reply when the requested id names no live match; <c>{0}</c> is the id.</summary>
	public const string NoActiveMatchWithHashId = "No active match with id #{0}.";

	/// <summary>
	///     Reply when the sender is not a referee of the targeted match, whether rejected by
	///     <c>!mp in</c> or by the general <c>!mp</c> referee gate; <c>{0}</c> is the match id.
	/// </summary>
	public const string NotARefereeOfMatch = "You're not a referee of match #{0}.";

	/// <summary>Reply after switching the scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public const string NowTargetingMatch = "Now targeting match #{0} {1}.";

	// ── !mp settings ─────────────────────────────────────────────────────────────────────────
	/// <summary>First <c>!mp settings</c> line; <c>{0}</c> is the room name, <c>{1}</c> the id.</summary>
	public const string SettingsRoomName = "Room name: {0} (#{1})";

	/// <summary>Beatmap line in <c>!mp settings</c>; <c>{0}</c> is the map id, <c>{1}</c> its full name.</summary>
	public const string SettingsBeatmap = "Beatmap: {0} {1}";

	/// <summary>Beatmap line in <c>!mp settings</c> when the stored map id resolves to nothing.</summary>
	public const string SettingsBeatmapNotFound = "Beatmap: Not found";

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

	/// <summary>IRC line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public const string SettingsIrc = "IRC: ({0})";

	// ── !mp lock / unlock / size ─────────────────────────────────────────────────────────────
	/// <summary>Reply after locking the room.</summary>
	public const string LockedMatch = "Locked the match";

	/// <summary>Reply after unlocking the room.</summary>
	public const string UnlockedMatch = "Unlocked the match";

	/// <summary>Usage line for <c>!mp size</c>.</summary>
	public const string SizeUsage = "Usage: !mp size <1-16>";

	/// <summary>Reply after resizing the room; <c>{0}</c> is the new slot count.</summary>
	public const string ChangedMatchSize = "Changed match to size {0}";

	// ── !mp move / host / clearhost ──────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp move</c>.</summary>
	public const string MoveUsage = "Usage: !mp move <name/id> <slot 1-16>";

	/// <summary>Reply when the target cannot be resolved to a seated player.</summary>
	public const string UserNotInMatchOrUnregistered = "User is not in this match or not registered.";

	/// <summary>Reply when the destination slot is occupied or otherwise not open.</summary>
	public const string DestinationSlotNotOpen = "Destination slot is not open.";

	/// <summary>Reply when the target holds no slot in the match; <c>{0}</c> is the target's name.</summary>
	public const string NotInThisMatch = "{0} is not in this match.";

	/// <summary>Reply after moving a player; <c>{0}</c> is the player's name, <c>{1}</c> the destination slot.</summary>
	public const string MovedToSlot = "Moved {0} into slot {1}";

	/// <summary>Usage line for <c>!mp host</c>.</summary>
	public const string HostUsage = "Usage: !mp host <name/id>";

	/// <summary>Reply after transferring host; <c>{0}</c> is the new host's name.</summary>
	public const string ChangedMatchHost = "Changed match host to {0}";

	/// <summary>Reply after clearing the host.</summary>
	public const string ClearedMatchHost = "Cleared match host";

	// ── !mp name / password / private ────────────────────────────────────────────────────────
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
		"The match is now private. It will be hidden from the lobby and only for invited.";

	/// <summary>Reply after making the room public.</summary>
	public const string MatchNowPublic = "The match is now public.";

	/// <summary>Usage line for <c>!mp private</c>.</summary>
	public const string PrivateUsage = "Usage: !mp private [0|1]";

	// ── !mp invite ───────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp invite</c>.</summary>
	public const string InviteUsage = "Usage: !mp invite <name/id>";

	/// <summary>Reply when the target is not connected with the osu! client.</summary>
	public const string InviteRequiresClient = "User must be connected with the osu! client to be invited.";

	/// <summary>Reply when the target is already seated in the room.</summary>
	public const string UserAlreadyInRoom = "User is already in the room";

	/// <summary>Reply when the target is BasilBot.</summary>
	public const string CannotInviteBot = "Cannot invite BasilBot.";

	/// <summary>Reply after inviting a player; <c>{0}</c> is the invitee's name.</summary>
	public const string InvitedToRoom = "Invited {0} to the room";

	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp addref</c>.</summary>
	public const string AddRefUsage = "Usage: !mp addref <name/id>";

	/// <summary>Reply when the target cannot be resolved to any user.</summary>
	public const string UserNotFound = "User not found";

	/// <summary>Reply when the target is BasilBot.</summary>
	public const string CannotAddBotReferee = "Cannot add BasilBot as a referee.";

	/// <summary>Reply after adding a referee; <c>{0}</c> is the new referee's name.</summary>
	public const string AddedReferee = "Added {0} to the match referees";

	/// <summary>Reply when the target already holds referee status; <c>{0}</c> is the target.</summary>
	public const string TargetIsAlreadyAReferee = "{0} is already a referee of this match.";

	/// <summary>Usage line for <c>!mp removeref</c>.</summary>
	public const string RemoveRefUsage = "Usage: !mp removeref <name/id>";

	/// <summary>Reply when removing the referee would leave the room without one; <c>{0}</c> is the target.</summary>
	public const string CannotRemoveLastReferee = "Cannot remove {0} - at least one referee must remain.";

	/// <summary>Reply when the target holds no referee status; <c>{0}</c> is the target.</summary>
	public const string TargetIsNotAReferee = "{0} is not a referee of this match.";

	/// <summary>Reply when the target is the match's creator; <c>{0}</c> is the target.</summary>
	public const string CannotRemoveCreator = "Cannot remove {0} - they created this match.";

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

	// ── !mp team / set ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp team</c> (id form).</summary>
	public const string TeamUsage = "Usage: !mp team <name/id> <red|blue>";

	/// <summary>Usage line for <c>!mp team</c> (name form).</summary>
	public const string TeamUsageName = "Usage: !mp team <name> <red|blue>";

	/// <summary>Reply after assigning a team; <c>{0}</c> is the player's name, <c>{1}</c> the team label.</summary>
	public const string MovedToTeam = "Moved {0} to team {1}";

	/// <summary>Usage line for <c>!mp set</c>.</summary>
	public const string SetUsage = "Usage: !mp set <teammode 0-3> [scoremode 0-3] [size 1-16]";

	/// <summary>
	///     Reply after changing settings; <c>{0}</c> is the team type, <c>{1}</c> the win condition, <c>{2}</c> a size
	///     suffix.
	/// </summary>
	public const string ChangedMatchSettings = "Changed match settings to {0}, {1}{2}";

	// ── !mp map / mods ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp map</c>.</summary>
	public const string MapUsage = "Usage: !mp map <beatmap id> [playmode]";

	/// <summary>Reply when no beatmap carries the requested id; <c>{0}</c> is the id.</summary>
	public const string NoBeatmapWithId = "No beatmap with ID {0} found.";

	/// <summary>Reply after changing the map; <c>{0}</c> artist, <c>{1}</c> title, <c>{2}</c> version.</summary>
	public const string ChangedBeatmap = "Changed beatmap to {0} - {1} [{2}]";

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

	// ── !mp start / timer / aborttimer / abort ───────────────────────────────────────────────
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

	// ── !mp kick / ban / unban / close ───────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp kick</c>.</summary>
	public const string KickUsage = "Usage: !mp kick <name/id>";

	/// <summary>Reply when the target is BasilBot.</summary>
	public const string CannotKickBot = "Cannot kick BasilBot.";

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public const string CannotKickReferee =
		"Cannot kick {0} — they're a referee. Remove referee status first with !mp removeref.";

	/// <summary>Reply after kicking a player; <c>{0}</c> is the kicked player's name.</summary>
	public const string KickedFromMatch = "Kicked {0} from the match";

	/// <summary>Usage line for <c>!mp ban</c>.</summary>
	public const string BanUsage = "Usage: !mp ban <name/id>";

	/// <summary>Reply when the target is not a registered user.</summary>
	public const string UserNotRegistered = "User is not registered.";

	/// <summary>Reply when the target is BasilBot.</summary>
	public const string CannotBanBot = "Cannot ban BasilBot.";

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public const string CannotBanReferee =
		"Cannot ban {0} — they're a referee. Remove referee status first with !mp removeref.";

	/// <summary>Reply after banning a player; <c>{0}</c> is the banned player's name.</summary>
	public const string BannedPlayerFromMatch = "Banned {0} from the match";

	/// <summary>Usage line for <c>!mp unban</c>.</summary>
	public const string UnbanUsage = "Usage: !mp unban <name/id>";

	/// <summary>Reply when the target is not banned; <c>{0}</c> is the target's name.</summary>
	public const string NotBannedFromMatch = "{0} is not banned from this match.";

	/// <summary>Reply after unbanning a player; <c>{0}</c> is the unbanned player's name.</summary>
	public const string UnbannedFromMatch = "Unbanned {0} from the match";

	/// <summary>Reply after closing the match.</summary>
	public const string ClosedMatch = "Closed the match";

	// ── scope routing (CommandDispatcher) ────────────────────────────────────────────────────
	/// <summary>Reply when a scoped subcommand resolves to no match at all.</summary>
	public const string NotScopedToAnyMatchHint =
		"You're not scoped to a match — use !mp make, !mp join <id>, or !mp in <id> first.";

	// ── !mp dispatch errors ──────────────────────────────────────────────────────────────────
	/// <summary>Reply when a subcommand name is not recognized; <c>{0}</c> is the subcommand.</summary>
	public const string UnknownMpSubcommand =
		"Unknown !mp subcommand: {0}. Use !mp help to list available subcommands.";

	/// <summary>Reply when a subcommand is limited to the match's creator; <c>{0}</c> is the subcommand.</summary>
	public const string CreatorOnlyMp = "Only the match's creator can run {0}.";

	/// <summary>Reply when a subcommand is not allowed from <c>#lobby</c>; <c>{0}</c> is the subcommand.</summary>
	public const string MpNotUsableFromLobby = "!mp {0} can't be used from #lobby.";

	/// <summary>Reply when a chained <c>!mp</c> line is issued from <c>#lobby</c>.</summary>
	public const string MpChainNotUsableFromLobby = "Chained !mp commands can't be used from #lobby.";

	/// <summary>Reply when <c>!mp in</c> is run from a channel instead of a DM to the bot.</summary>
	public const string MpInDmOnly = "!mp in only works in a DM to BasilBot.";

	// ── !where / !faq / !roll / chain rejection ──────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public const string WhereUsage = "Usage: !where <username>";

	/// <summary>Reply when the named user is not registered; <c>{0}</c> is the name.</summary>
	public const string NotRegistered = "{0} is not registered.";

	/// <summary>Reply reporting a user's country; <c>{0}</c> is the name, <c>{1}</c> the country.</summary>
	public const string WhereIsIn = "{0} is in {1}";

	/// <summary>Usage line for <c>!faq</c>.</summary>
	public const string FaqUsage = "Usage: !faq <entry>|list";

	/// <summary>Reply when no FAQ entry matches; <c>{0}</c> is the requested entry.</summary>
	public const string NoFaqEntryFound = "No FAQ entry found for '{0}'.";

	/// <summary>Reply when the FAQ has no entries.</summary>
	public const string NoFaqEntriesAvailable = "No FAQ entries available.";

	/// <summary>Reply listing the available FAQ entries; <c>{0}</c> is the comma-joined list.</summary>
	public const string AvailableFaqEntries = "Available FAQ entries: {0}";

	/// <summary>
	///     Reply rejecting a chain segment that is not an <c>!mp</c> command; <c>{0}</c> is the prefix, <c>{1}</c> the
	///     rejected text.
	/// </summary>
	public const string ChainMustBeMp = "Chained commands must all be `{0}mp <subcommand>` — rejected at: '{1}'.";

	/// <summary>
	///     Reply rejecting a chain segment that cannot be chained; <c>{0}</c> is the prefix, <c>{1}</c> the subcommand,
	///     <c>{2}</c> the rejected text.
	/// </summary>
	public const string CannotChainMp = "`{0}mp {1}` can't be chained — rejected at: '{2}'.";

	/// <summary>Reply to <c>!roll</c>; <c>{0}</c> is the sender's name, <c>{1}</c> the roll result.</summary>
	public const string RollResult = "{0} rolls {1} point(s)";
}