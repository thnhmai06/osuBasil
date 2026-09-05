using Basil.Application.Abstractions.Bot;

namespace Basil.Application.Services.Bot;

/// <summary>
///     The user-visible reply text sent by the BasilBot chat commands, the single source of truth
///     for that surface.
/// </summary>
/// <remarks>
///     Every reply the bot sends through an <see cref="ICommandReplySink" /> — <c>!mp</c> and
///     <c>!where</c>/<c>!faq</c>/<c>!roll</c>/<c>!help</c> — is a named member here. Production
///     services read these members and tests assert against the same symbols, so the two cannot
///     drift. The wording itself lives outside the code, in <see cref="ReplyLocale" />'s
///     <c>BasilBot.json</c>, so it can be edited without a rebuild; each member's name, plus the
///     <c>// ──</c> comment group it sits under, is the file's <c>Category.Member</c> lookup key
///     (via <see cref="ReplyLocale.BasilBot" />), so a member and its wording cannot silently fall
///     out of sync. Format strings use <see cref="string.Format(string, object?[])" /> placeholders;
///     fixed strings are plain text. Deliberately changing the wording of a reply is a
///     public-behavior change: an edit to the locale file, and every test that pins it stays in
///     sync.
/// </remarks>
public static class MpReplies
{
	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────────
	/// <summary>Reply when a room could not be created.</summary>
	public static readonly string CreateFailed = ReplyLocale.BasilBot($"Make.{nameof(CreateFailed)}");

	/// <summary>Reply after a room is created; <c>{0}</c> is the room id, <c>{1}</c> its name, <c>{2}</c> a privacy suffix.</summary>
	public static readonly string CreatedMatch = ReplyLocale.BasilBot($"Make.{nameof(CreatedMatch)}");

	// ── !mp join ─────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp join</c>.</summary>
	public static readonly string JoinUsage = ReplyLocale.BasilBot($"Join.{nameof(JoinUsage)}");

	/// <summary>Reply when no live match carries the requested id; <c>{0}</c> is the id.</summary>
	public static readonly string NoActiveMatchWithId = ReplyLocale.BasilBot($"Join.{nameof(NoActiveMatchWithId)}");

	/// <summary>Reply when a private room rejects a non-invitee; <c>{0}</c> is the room id.</summary>
	public static readonly string PrivateRoomJoinDenied = ReplyLocale.BasilBot($"Join.{nameof(PrivateRoomJoinDenied)}");

	/// <summary>Reply when the sender is already seated in another match.</summary>
	public static readonly string AlreadyInAMatch = ReplyLocale.BasilBot($"Join.{nameof(AlreadyInAMatch)}");

	/// <summary>Reply when the sender is banned from the match.</summary>
	public static readonly string BannedFromMatch = ReplyLocale.BasilBot($"Join.{nameof(BannedFromMatch)}");

	/// <summary>Reply after joining a match's slots; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string JoinedMatch = ReplyLocale.BasilBot($"Join.{nameof(JoinedMatch)}");

	/// <summary>Reply when the supplied password is wrong.</summary>
	public static readonly string IncorrectPassword = ReplyLocale.BasilBot($"Join.{nameof(IncorrectPassword)}");

	/// <summary>Reply when every slot is taken.</summary>
	public static readonly string MatchIsFull = ReplyLocale.BasilBot($"Join.{nameof(MatchIsFull)}");

	/// <summary>Reply when the room is locked.</summary>
	public static readonly string MatchIsLocked = ReplyLocale.BasilBot($"Join.{nameof(MatchIsLocked)}");

	/// <summary>Reply when the join failed for an otherwise-unreported reason.</summary>
	public static readonly string FailedToJoinMatch = ReplyLocale.BasilBot($"Join.{nameof(FailedToJoinMatch)}");

	/// <summary>Reply when an IRC session could not join the match's chat channel.</summary>
	public static readonly string FailedToJoinMatchChat = ReplyLocale.BasilBot($"Join.{nameof(FailedToJoinMatchChat)}");

	/// <summary>Reply after an IRC session joins a match's chat; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string JoinedMatchChat = ReplyLocale.BasilBot($"Join.{nameof(JoinedMatchChat)}");

	// ── !mp in ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when the sender has no stored scope and is not in any match.</summary>
	public static readonly string NotScopedToAnyMatch = ReplyLocale.BasilBot($"In.{nameof(NotScopedToAnyMatch)}");

	/// <summary>
	///     Reply when the stored scope points at a match that is no longer live; <c>{0}</c> is the id.
	/// </summary>
	/// <remarks>
	///     Says "no longer live", not "no longer exists": the match's row still exists (it can be
	///     read back via <c>GET /matches/{id}</c>), it's just no longer in the in-memory registry
	///     this lookup checks.
	/// </remarks>
	public static readonly string WasScopedToGoneMatch = ReplyLocale.BasilBot($"In.{nameof(WasScopedToGoneMatch)}");

	/// <summary>Reply reporting the current scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string CurrentlyScopedToMatch = ReplyLocale.BasilBot($"In.{nameof(CurrentlyScopedToMatch)}");

	/// <summary>Header line before the list of matches the sender referees.</summary>
	public static readonly string YouAreARefereeOf = ReplyLocale.BasilBot($"In.{nameof(YouAreARefereeOf)}");

	/// <summary>Usage line for <c>!mp in</c>.</summary>
	public static readonly string InUsage = ReplyLocale.BasilBot($"In.{nameof(InUsage)}");

	/// <summary>Reply when the requested id names no live match; <c>{0}</c> is the id.</summary>
	public static readonly string NoActiveMatchWithHashId = ReplyLocale.BasilBot($"In.{nameof(NoActiveMatchWithHashId)}");

	/// <summary>
	///     Reply when the sender is not a referee of the targeted match, whether rejected by
	///     <c>!mp in</c> or by the general <c>!mp</c> referee gate; <c>{0}</c> is the match id.
	/// </summary>
	public static readonly string NotARefereeOfMatch = ReplyLocale.BasilBot($"In.{nameof(NotARefereeOfMatch)}");

	/// <summary>Reply after switching the scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string NowTargetingMatch = ReplyLocale.BasilBot($"In.{nameof(NowTargetingMatch)}");

	// ── !mp settings ─────────────────────────────────────────────────────────────────────────
	/// <summary>First <c>!mp settings</c> line; <c>{0}</c> is the room name, <c>{1}</c> the id.</summary>
	public static readonly string SettingsRoomName = ReplyLocale.BasilBot($"Settings.{nameof(SettingsRoomName)}");

	/// <summary>Beatmap line in <c>!mp settings</c>; <c>{0}</c> is the map id, <c>{1}</c> its full name.</summary>
	public static readonly string SettingsBeatmap = ReplyLocale.BasilBot($"Settings.{nameof(SettingsBeatmap)}");

	/// <summary>Beatmap line in <c>!mp settings</c> when the stored map id resolves to nothing.</summary>
	public static readonly string SettingsBeatmapNotFound = ReplyLocale.BasilBot($"Settings.{nameof(SettingsBeatmapNotFound)}");

	/// <summary>Beatmap line in <c>!mp settings</c> when no beatmap has been selected at all.</summary>
	public static readonly string SettingsBeatmapNotSelected = ReplyLocale.BasilBot($"Settings.{nameof(SettingsBeatmapNotSelected)}");

	/// <summary>Second <c>!mp settings</c> line; <c>{0}</c> is the team type, <c>{1}</c> the win condition.</summary>
	public static readonly string SettingsTeamMode = ReplyLocale.BasilBot($"Settings.{nameof(SettingsTeamMode)}");

	/// <summary>Mods line in <c>!mp settings</c>; <c>{0}</c> is the comma-joined mod list.</summary>
	public static readonly string SettingsActiveMods = ReplyLocale.BasilBot($"Settings.{nameof(SettingsActiveMods)}");

	/// <summary>Creator line in <c>!mp settings</c>; <c>{0}</c> is the user id, <c>{1}</c> the name.</summary>
	public static readonly string SettingsCreator = ReplyLocale.BasilBot($"Settings.{nameof(SettingsCreator)}");

	/// <summary>Player-count line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public static readonly string SettingsPlayers = ReplyLocale.BasilBot($"Settings.{nameof(SettingsPlayers)}");

	/// <summary>IRC line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public static readonly string SettingsIrc = ReplyLocale.BasilBot($"Settings.{nameof(SettingsIrc)}");

	// ── !mp lock / unlock / size ─────────────────────────────────────────────────────────────
	/// <summary>Reply after locking the room.</summary>
	public static readonly string LockedMatch = ReplyLocale.BasilBot($"Lock.{nameof(LockedMatch)}");

	/// <summary>Reply after unlocking the room.</summary>
	public static readonly string UnlockedMatch = ReplyLocale.BasilBot($"Lock.{nameof(UnlockedMatch)}");

	/// <summary>Usage line for <c>!mp size</c>.</summary>
	public static readonly string SizeUsage = ReplyLocale.BasilBot($"Lock.{nameof(SizeUsage)}");

	/// <summary>Reply after resizing the room; <c>{0}</c> is the new slot count.</summary>
	public static readonly string ChangedMatchSize = ReplyLocale.BasilBot($"Lock.{nameof(ChangedMatchSize)}");

	// ── !mp move / host / clearhost ──────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp move</c>.</summary>
	public static readonly string MoveUsage = ReplyLocale.BasilBot($"Move.{nameof(MoveUsage)}");

	/// <summary>Reply when the target cannot be resolved to a seated player.</summary>
	public static readonly string UserNotInMatchOrUnregistered = ReplyLocale.BasilBot($"Move.{nameof(UserNotInMatchOrUnregistered)}");

	/// <summary>Reply when the destination slot is occupied or otherwise not open.</summary>
	public static readonly string DestinationSlotNotOpen = ReplyLocale.BasilBot($"Move.{nameof(DestinationSlotNotOpen)}");

	/// <summary>Reply when the target holds no slot in the match; <c>{0}</c> is the target's name.</summary>
	public static readonly string NotInThisMatch = ReplyLocale.BasilBot($"Move.{nameof(NotInThisMatch)}");

	/// <summary>Reply after moving a player; <c>{0}</c> is the player's name, <c>{1}</c> the destination slot.</summary>
	public static readonly string MovedToSlot = ReplyLocale.BasilBot($"Move.{nameof(MovedToSlot)}");

	/// <summary>Usage line for <c>!mp host</c>.</summary>
	public static readonly string HostUsage = ReplyLocale.BasilBot($"Move.{nameof(HostUsage)}");

	/// <summary>Reply after transferring host; <c>{0}</c> is the new host's name.</summary>
	public static readonly string ChangedMatchHost = ReplyLocale.BasilBot($"Move.{nameof(ChangedMatchHost)}");

	/// <summary>Reply after clearing the host.</summary>
	public static readonly string ClearedMatchHost = ReplyLocale.BasilBot($"Move.{nameof(ClearedMatchHost)}");

	// ── !mp name / password / private ────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp name</c>.</summary>
	public static readonly string NameUsage = ReplyLocale.BasilBot($"Name.{nameof(NameUsage)}");

	/// <summary>Reply after renaming the room; <c>{0}</c> is the new name.</summary>
	public static readonly string RoomNameUpdated = ReplyLocale.BasilBot($"Name.{nameof(RoomNameUpdated)}");

	/// <summary>Reply after clearing the room password.</summary>
	public static readonly string RemovedMatchPassword = ReplyLocale.BasilBot($"Name.{nameof(RemovedMatchPassword)}");

	/// <summary>Reply after changing the room password.</summary>
	public static readonly string ChangedMatchPassword = ReplyLocale.BasilBot($"Name.{nameof(ChangedMatchPassword)}");

	/// <summary>Reply reporting the room's privacy; <c>{0}</c> is <c>private</c> or <c>not private</c>.</summary>
	public static readonly string MatchIsPrivateNow = ReplyLocale.BasilBot($"Name.{nameof(MatchIsPrivateNow)}");

	/// <summary>Reply after making the room private.</summary>
	public static readonly string MatchNowPrivate = ReplyLocale.BasilBot($"Name.{nameof(MatchNowPrivate)}");

	/// <summary>Reply after making the room public.</summary>
	public static readonly string MatchNowPublic = ReplyLocale.BasilBot($"Name.{nameof(MatchNowPublic)}");

	/// <summary>Usage line for <c>!mp private</c>.</summary>
	public static readonly string PrivateUsage = ReplyLocale.BasilBot($"Name.{nameof(PrivateUsage)}");

	// ── !mp invite ───────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp invite</c>.</summary>
	public static readonly string InviteUsage = ReplyLocale.BasilBot($"Invite.{nameof(InviteUsage)}");

	/// <summary>Reply when the target is not connected with the osu! client.</summary>
	public static readonly string InviteRequiresClient = ReplyLocale.BasilBot($"Invite.{nameof(InviteRequiresClient)}");

	/// <summary>Reply when the target is already seated in the room.</summary>
	public static readonly string UserAlreadyInRoom = ReplyLocale.BasilBot($"Invite.{nameof(UserAlreadyInRoom)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotInviteBot = ReplyLocale.BasilBot($"Invite.{nameof(CannotInviteBot)}");

	/// <summary>Reply after inviting a player; <c>{0}</c> is the invitee's name.</summary>
	public static readonly string InvitedToRoom = ReplyLocale.BasilBot($"Invite.{nameof(InvitedToRoom)}");

	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp addref</c>.</summary>
	public static readonly string AddRefUsage = ReplyLocale.BasilBot($"Referee.{nameof(AddRefUsage)}");

	/// <summary>Reply when the target cannot be resolved to any user.</summary>
	public static readonly string UserNotFound = ReplyLocale.BasilBot($"Referee.{nameof(UserNotFound)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotAddBotReferee = ReplyLocale.BasilBot($"Referee.{nameof(CannotAddBotReferee)}");

	/// <summary>Reply after adding a referee; <c>{0}</c> is the new referee's name.</summary>
	public static readonly string AddedReferee = ReplyLocale.BasilBot($"Referee.{nameof(AddedReferee)}");

	/// <summary>Reply when the target already holds referee status; <c>{0}</c> is the target.</summary>
	public static readonly string TargetIsAlreadyAReferee = ReplyLocale.BasilBot($"Referee.{nameof(TargetIsAlreadyAReferee)}");

	/// <summary>Usage line for <c>!mp removeref</c>.</summary>
	public static readonly string RemoveRefUsage = ReplyLocale.BasilBot($"Referee.{nameof(RemoveRefUsage)}");

	/// <summary>Reply when removing the referee would leave the room without one; <c>{0}</c> is the target.</summary>
	public static readonly string CannotRemoveLastReferee = ReplyLocale.BasilBot($"Referee.{nameof(CannotRemoveLastReferee)}");

	/// <summary>Reply when the target holds no referee status; <c>{0}</c> is the target.</summary>
	public static readonly string TargetIsNotAReferee = ReplyLocale.BasilBot($"Referee.{nameof(TargetIsNotAReferee)}");

	/// <summary>Reply when the target is the match's creator; <c>{0}</c> is the target.</summary>
	public static readonly string CannotRemoveCreator = ReplyLocale.BasilBot($"Referee.{nameof(CannotRemoveCreator)}");

	/// <summary>Reply after removing a referee; <c>{0}</c> is the removed referee's name.</summary>
	public static readonly string RemovedReferee = ReplyLocale.BasilBot($"Referee.{nameof(RemovedReferee)}");

	/// <summary>Reply when the match has no referees.</summary>
	public static readonly string NoReferees = ReplyLocale.BasilBot($"Referee.{nameof(NoReferees)}");

	/// <summary>Header line before the list of referees.</summary>
	public static readonly string MatchReferees = ReplyLocale.BasilBot($"Referee.{nameof(MatchReferees)}");

	/// <summary>Reply when the match has no banned players.</summary>
	public static readonly string NoBannedPlayers = ReplyLocale.BasilBot($"Referee.{nameof(NoBannedPlayers)}");

	/// <summary>Header line before the list of banned players.</summary>
	public static readonly string MatchBans = ReplyLocale.BasilBot($"Referee.{nameof(MatchBans)}");

	// ── !mp team / set ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp team</c> (id form).</summary>
	public static readonly string TeamUsage = ReplyLocale.BasilBot($"Team.{nameof(TeamUsage)}");

	/// <summary>Usage line for <c>!mp team</c> (name form).</summary>
	public static readonly string TeamUsageName = ReplyLocale.BasilBot($"Team.{nameof(TeamUsageName)}");

	/// <summary>Reply after assigning a team; <c>{0}</c> is the player's name, <c>{1}</c> the team label.</summary>
	public static readonly string MovedToTeam = ReplyLocale.BasilBot($"Team.{nameof(MovedToTeam)}");

	/// <summary>Usage line for <c>!mp set</c>.</summary>
	public static readonly string SetUsage = ReplyLocale.BasilBot($"Team.{nameof(SetUsage)}");

	/// <summary>
	///     Reply after changing settings; <c>{0}</c> is the team type, <c>{1}</c> the win condition, <c>{2}</c> a size
	///     suffix.
	/// </summary>
	public static readonly string ChangedMatchSettings = ReplyLocale.BasilBot($"Team.{nameof(ChangedMatchSettings)}");

	// ── !mp map / mods ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp map</c>.</summary>
	public static readonly string MapUsage = ReplyLocale.BasilBot($"Map.{nameof(MapUsage)}");

	/// <summary>Reply when no beatmap carries the requested id; <c>{0}</c> is the id.</summary>
	public static readonly string NoBeatmapWithId = ReplyLocale.BasilBot($"Map.{nameof(NoBeatmapWithId)}");

	/// <summary>Reply after changing the map; <c>{0}</c> artist, <c>{1}</c> title, <c>{2}</c> version.</summary>
	public static readonly string ChangedBeatmap = ReplyLocale.BasilBot($"Map.{nameof(ChangedBeatmap)}");

	/// <summary>Usage line for <c>!mp mods</c>.</summary>
	public static readonly string ModsUsage = ReplyLocale.BasilBot($"Map.{nameof(ModsUsage)}");

	/// <summary>Mod-change summary, enabled case; <c>{0}</c> is the mod list.</summary>
	public static readonly string EnabledMods = ReplyLocale.BasilBot($"Map.{nameof(EnabledMods)}");

	/// <summary>Mod-change summary, disabled case; <c>{0}</c> is the mod list.</summary>
	public static readonly string DisabledMods = ReplyLocale.BasilBot($"Map.{nameof(DisabledMods)}");

	/// <summary>Mod-change summary line when freemod was turned off.</summary>
	public static readonly string DisabledFreemod = ReplyLocale.BasilBot($"Map.{nameof(DisabledFreemod)}");

	/// <summary>Mod-change summary line when freemod was turned on.</summary>
	public static readonly string EnabledFreemod = ReplyLocale.BasilBot($"Map.{nameof(EnabledFreemod)}");

	/// <summary>Mod-change summary when nothing changed.</summary>
	public static readonly string NoModChanges = ReplyLocale.BasilBot($"Map.{nameof(NoModChanges)}");

	// ── !mp start / timer / aborttimer / abort ───────────────────────────────────────────────
	/// <summary>Reply when the match is already in progress.</summary>
	public static readonly string MatchAlreadyInProgress = ReplyLocale.BasilBot($"Start.{nameof(MatchAlreadyInProgress)}");

	/// <summary>Reply when a start countdown was queued; <c>{0}</c> is the delay in seconds.</summary>
	public static readonly string MatchStartsInSeconds = ReplyLocale.BasilBot($"Start.{nameof(MatchStartsInSeconds)}");

	/// <summary>Reply after starting the match immediately.</summary>
	public static readonly string MatchStarted = ReplyLocale.BasilBot($"Start.{nameof(MatchStarted)}");

	/// <summary>Usage line for <c>!mp timer</c>.</summary>
	public static readonly string TimerUsage = ReplyLocale.BasilBot($"Start.{nameof(TimerUsage)}");

	/// <summary>Reply after starting a countdown; <c>{0}</c> is the duration in seconds.</summary>
	public static readonly string CountdownStarted = ReplyLocale.BasilBot($"Start.{nameof(CountdownStarted)}");

	/// <summary>Reply when no countdown is running.</summary>
	public static readonly string NoCountdownRunning = ReplyLocale.BasilBot($"Start.{nameof(NoCountdownRunning)}");

	/// <summary>Reply after aborting a countdown.</summary>
	public static readonly string CountdownAborted = ReplyLocale.BasilBot($"Start.{nameof(CountdownAborted)}");

	/// <summary>Reply when the match is not in progress.</summary>
	public static readonly string MatchNotInProgress = ReplyLocale.BasilBot($"Start.{nameof(MatchNotInProgress)}");

	/// <summary>Reply after aborting the match.</summary>
	public static readonly string AbortedMatch = ReplyLocale.BasilBot($"Start.{nameof(AbortedMatch)}");

	// ── !mp kick / ban / unban / close ───────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp kick</c>.</summary>
	public static readonly string KickUsage = ReplyLocale.BasilBot($"Moderation.{nameof(KickUsage)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotKickBot = ReplyLocale.BasilBot($"Moderation.{nameof(CannotKickBot)}");

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public static readonly string CannotKickReferee = ReplyLocale.BasilBot($"Moderation.{nameof(CannotKickReferee)}");

	/// <summary>Reply after kicking a player; <c>{0}</c> is the kicked player's name.</summary>
	public static readonly string KickedFromMatch = ReplyLocale.BasilBot($"Moderation.{nameof(KickedFromMatch)}");

	/// <summary>Usage line for <c>!mp ban</c>.</summary>
	public static readonly string BanUsage = ReplyLocale.BasilBot($"Moderation.{nameof(BanUsage)}");

	/// <summary>Reply when the target is not a registered user.</summary>
	public static readonly string UserNotRegistered = ReplyLocale.BasilBot($"Moderation.{nameof(UserNotRegistered)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotBanBot = ReplyLocale.BasilBot($"Moderation.{nameof(CannotBanBot)}");

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public static readonly string CannotBanReferee = ReplyLocale.BasilBot($"Moderation.{nameof(CannotBanReferee)}");

	/// <summary>Reply after banning a player; <c>{0}</c> is the banned player's name.</summary>
	public static readonly string BannedPlayerFromMatch = ReplyLocale.BasilBot($"Moderation.{nameof(BannedPlayerFromMatch)}");

	/// <summary>Usage line for <c>!mp unban</c>.</summary>
	public static readonly string UnbanUsage = ReplyLocale.BasilBot($"Moderation.{nameof(UnbanUsage)}");

	/// <summary>Reply when the target is not banned; <c>{0}</c> is the target's name.</summary>
	public static readonly string NotBannedFromMatch = ReplyLocale.BasilBot($"Moderation.{nameof(NotBannedFromMatch)}");

	/// <summary>Reply after unbanning a player; <c>{0}</c> is the unbanned player's name.</summary>
	public static readonly string UnbannedFromMatch = ReplyLocale.BasilBot($"Moderation.{nameof(UnbannedFromMatch)}");

	/// <summary>Reply after closing the match.</summary>
	public static readonly string ClosedMatch = ReplyLocale.BasilBot($"Moderation.{nameof(ClosedMatch)}");

	// ── scope routing (CommandDispatcher) ────────────────────────────────────────────────────
	/// <summary>Reply when a scoped subcommand resolves to no match at all.</summary>
	public static readonly string NotScopedToAnyMatchHint = ReplyLocale.BasilBot($"Dispatch.{nameof(NotScopedToAnyMatchHint)}");

	// ── !mp dispatch errors ──────────────────────────────────────────────────────────────────
	/// <summary>Reply when a subcommand name is not recognized; <c>{0}</c> is the subcommand.</summary>
	public static readonly string UnknownMpSubcommand = ReplyLocale.BasilBot($"Dispatch.{nameof(UnknownMpSubcommand)}");

	/// <summary>Reply when a subcommand is limited to the match's creator; <c>{0}</c> is the subcommand.</summary>
	public static readonly string CreatorOnlyMp = ReplyLocale.BasilBot($"Dispatch.{nameof(CreatorOnlyMp)}");

	/// <summary>Reply when a subcommand is not allowed from <c>#lobby</c>; <c>{0}</c> is the subcommand.</summary>
	public static readonly string MpNotUsableFromLobby = ReplyLocale.BasilBot($"Dispatch.{nameof(MpNotUsableFromLobby)}");

	/// <summary>Reply when a chained <c>!mp</c> line is issued from <c>#lobby</c>.</summary>
	public static readonly string MpChainNotUsableFromLobby = ReplyLocale.BasilBot($"Dispatch.{nameof(MpChainNotUsableFromLobby)}");

	/// <summary>Reply when <c>!mp in</c> is run from a channel instead of a DM to the bot.</summary>
	public static readonly string MpInDmOnly = ReplyLocale.BasilBot($"Dispatch.{nameof(MpInDmOnly)}");

	// ── !where / !faq / !roll / chain rejection ──────────────────────────────────────────────
	/// <summary>Usage line for <c>!where</c>.</summary>
	public static readonly string WhereUsage = ReplyLocale.BasilBot($"General.{nameof(WhereUsage)}");

	/// <summary>Reply when the named user is not registered; <c>{0}</c> is the name.</summary>
	public static readonly string NotRegistered = ReplyLocale.BasilBot($"General.{nameof(NotRegistered)}");

	/// <summary>Reply reporting a user's country; <c>{0}</c> is the name, <c>{1}</c> the country.</summary>
	public static readonly string WhereIsIn = ReplyLocale.BasilBot($"General.{nameof(WhereIsIn)}");

	/// <summary>Usage line for <c>!faq</c>.</summary>
	public static readonly string FaqUsage = ReplyLocale.BasilBot($"General.{nameof(FaqUsage)}");

	/// <summary>Reply when no FAQ entry matches; <c>{0}</c> is the requested entry.</summary>
	public static readonly string NoFaqEntryFound = ReplyLocale.BasilBot($"General.{nameof(NoFaqEntryFound)}");

	/// <summary>Reply when the FAQ has no entries.</summary>
	public static readonly string NoFaqEntriesAvailable = ReplyLocale.BasilBot($"General.{nameof(NoFaqEntriesAvailable)}");

	/// <summary>Reply listing the available FAQ entries; <c>{0}</c> is the comma-joined list.</summary>
	public static readonly string AvailableFaqEntries = ReplyLocale.BasilBot($"General.{nameof(AvailableFaqEntries)}");

	/// <summary>
	///     Reply rejecting a chain segment that is not an <c>!mp</c> command; <c>{0}</c> is the prefix, <c>{1}</c> the
	///     rejected text.
	/// </summary>
	public static readonly string ChainMustBeMp = ReplyLocale.BasilBot($"General.{nameof(ChainMustBeMp)}");

	/// <summary>
	///     Reply rejecting a chain segment that cannot be chained; <c>{0}</c> is the prefix, <c>{1}</c> the subcommand,
	///     <c>{2}</c> the rejected text.
	/// </summary>
	public static readonly string CannotChainMp = ReplyLocale.BasilBot($"General.{nameof(CannotChainMp)}");

	/// <summary>Reply to <c>!roll</c>; <c>{0}</c> is the sender's name, <c>{1}</c> the roll result.</summary>
	public static readonly string RollResult = ReplyLocale.BasilBot($"General.{nameof(RollResult)}");
}