using Basil.Server.Features.Bot;
using Basil.Server.Shared.Localization;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     The user-visible reply text sent by the <c>!mp</c> command surface, the single source of
///     truth for that surface.
/// </summary>
/// <remarks>
///     Every reply <c>!mp</c> sends through an <see cref="ICommandReplySink" /> is a named member
///     here (the non-<c>!mp</c> commands <c>!where</c>/<c>!faq</c>/<c>!roll</c> have their own
///     holder, <c>Bot.BotReplies</c>). Production services read these members and tests assert
///     against the same symbols, so the two cannot drift. The wording itself lives outside the
///     code, in this slice's <c>Locale/mp.en.json</c> fragment, so it can be edited without a
///     rebuild; each member's lookup key (via <see cref="LocaleCatalog.Get" />) is a dotted key
///     mirroring the command it belongs to, so a member and its wording cannot silently fall out of
///     sync. Format strings use <see cref="string.Format(string, object?[])" /> placeholders; fixed
///     strings are plain text. Deliberately changing the wording of a reply is a public-behavior
///     change: an edit to the locale file, and every test that pins it stays in sync.
/// </remarks>
public static class MpReplies
{
	// ── !mp make / makeprivate ───────────────────────────────────────────────────────────────
	/// <summary>Reply when a room could not be created.</summary>
	public static readonly string CreateFailed = LocaleCatalog.Get($"Commands.Mp.Make.{nameof(CreateFailed)}");

	/// <summary>Reply after a room is created; <c>{0}</c> is the room id, <c>{1}</c> its name, <c>{2}</c> a privacy suffix.</summary>
	public static readonly string CreatedMatch = LocaleCatalog.Get($"Commands.Mp.Make.{nameof(CreatedMatch)}");

	// ── !mp join ─────────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp join</c>.</summary>
	public static readonly string JoinUsage = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(JoinUsage)}");

	/// <summary>Reply when no live match carries the requested id; <c>{0}</c> is the id.</summary>
	public static readonly string NoActiveMatchWithId =
		LocaleCatalog.Get($"Commands.Mp.Join.{nameof(NoActiveMatchWithId)}");

	/// <summary>Reply when a private room rejects a non-invitee; <c>{0}</c> is the room id.</summary>
	public static readonly string PrivateRoomJoinDenied =
		LocaleCatalog.Get($"Commands.Mp.Join.{nameof(PrivateRoomJoinDenied)}");

	/// <summary>Reply when the sender is already seated in another match.</summary>
	public static readonly string AlreadyInAMatch = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(AlreadyInAMatch)}");

	/// <summary>Reply when the sender is banned from the match.</summary>
	public static readonly string BannedFromMatch = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(BannedFromMatch)}");

	/// <summary>Reply after joining a match's slots; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string JoinedMatch = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(JoinedMatch)}");

	/// <summary>Reply when the supplied password is wrong.</summary>
	public static readonly string
		IncorrectPassword = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(IncorrectPassword)}");

	/// <summary>Reply when every slot is taken.</summary>
	public static readonly string MatchIsFull = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(MatchIsFull)}");

	/// <summary>Reply when the room is locked.</summary>
	public static readonly string MatchIsLocked = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(MatchIsLocked)}");

	/// <summary>Reply when the join failed for an otherwise-unreported reason.</summary>
	public static readonly string
		FailedToJoinMatch = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(FailedToJoinMatch)}");

	/// <summary>Reply when an IRC session could not join the match's chat channel.</summary>
	public static readonly string FailedToJoinMatchChat =
		LocaleCatalog.Get($"Commands.Mp.Join.{nameof(FailedToJoinMatchChat)}");

	/// <summary>Reply after an IRC session joins a match's chat; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string JoinedMatchChat = LocaleCatalog.Get($"Commands.Mp.Join.{nameof(JoinedMatchChat)}");

	// ── !mp in ───────────────────────────────────────────────────────────────────────────────
	/// <summary>Reply when the sender has no stored scope and is not in any match.</summary>
	public static readonly string NotScopedToAnyMatch =
		LocaleCatalog.Get($"Commands.Mp.In.{nameof(NotScopedToAnyMatch)}");

	/// <summary>
	///     Reply when the stored scope points at a match that is no longer live; <c>{0}</c> is the id.
	/// </summary>
	/// <remarks>
	///     Says "no longer live", not "no longer exists": the match's row still exists (it can be
	///     read back via <c>GET /matches/{id}</c>), it's just no longer in the in-memory registry
	///     this lookup checks.
	/// </remarks>
	public static readonly string WasScopedToGoneMatch =
		LocaleCatalog.Get($"Commands.Mp.In.{nameof(WasScopedToGoneMatch)}");

	/// <summary>Reply reporting the current scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string CurrentlyScopedToMatch =
		LocaleCatalog.Get($"Commands.Mp.In.{nameof(CurrentlyScopedToMatch)}");

	/// <summary>Header line before the list of matches the sender referees.</summary>
	public static readonly string YouAreARefereeOf = LocaleCatalog.Get($"Commands.Mp.In.{nameof(YouAreARefereeOf)}");

	/// <summary>Usage line for <c>!mp in</c>.</summary>
	public static readonly string InUsage = LocaleCatalog.Get($"Commands.Mp.In.{nameof(InUsage)}");

	/// <summary>Reply when the requested id names no live match; <c>{0}</c> is the id.</summary>
	public static readonly string NoActiveMatchWithHashId =
		LocaleCatalog.Get($"Commands.Mp.In.{nameof(NoActiveMatchWithHashId)}");

	/// <summary>
	///     Reply when the sender is not a referee of the targeted match, whether rejected by
	///     <c>!mp in</c> or by the general <c>!mp</c> referee gate; <c>{0}</c> is the match id.
	/// </summary>
	public static readonly string NotARefereeOfMatch =
		LocaleCatalog.Get($"Commands.Mp.In.{nameof(NotARefereeOfMatch)}");

	/// <summary>Reply after switching the scope; <c>{0}</c> is the room id, <c>{1}</c> its name.</summary>
	public static readonly string NowTargetingMatch = LocaleCatalog.Get($"Commands.Mp.In.{nameof(NowTargetingMatch)}");

	// ── !mp settings ─────────────────────────────────────────────────────────────────────────
	/// <summary>First <c>!mp settings</c> line; <c>{0}</c> is the room name, <c>{1}</c> the id.</summary>
	public static readonly string SettingsRoomName =
		LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsRoomName)}");

	/// <summary>Beatmap line in <c>!mp settings</c>; <c>{0}</c> is the map id, <c>{1}</c> its full name.</summary>
	public static readonly string
		SettingsBeatmap = LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsBeatmap)}");

	/// <summary>Beatmap line in <c>!mp settings</c> when the stored map id resolves to nothing.</summary>
	public static readonly string SettingsBeatmapNotFound =
		LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsBeatmapNotFound)}");

	/// <summary>Beatmap line in <c>!mp settings</c> when no beatmap has been selected at all.</summary>
	public static readonly string SettingsBeatmapNotSelected =
		LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsBeatmapNotSelected)}");

	/// <summary>Second <c>!mp settings</c> line; <c>{0}</c> is the team type, <c>{1}</c> the win condition.</summary>
	public static readonly string SettingsTeamMode =
		LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsTeamMode)}");

	/// <summary>Mods line in <c>!mp settings</c>; <c>{0}</c> is the comma-joined mod list.</summary>
	public static readonly string SettingsActiveMods =
		LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsActiveMods)}");

	/// <summary>Creator line in <c>!mp settings</c>; <c>{0}</c> is the user id, <c>{1}</c> the name.</summary>
	public static readonly string
		SettingsCreator = LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsCreator)}");

	/// <summary>Player-count line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public static readonly string
		SettingsPlayers = LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsPlayers)}");

	/// <summary>IRC line in <c>!mp settings</c>; <c>{0}</c> is the count.</summary>
	public static readonly string SettingsIrc = LocaleCatalog.Get($"Commands.Mp.Settings.{nameof(SettingsIrc)}");

	// ── !mp lock / unlock / size ─────────────────────────────────────────────────────────────
	/// <summary>Reply after locking the room.</summary>
	public static readonly string LockedMatch = LocaleCatalog.Get($"Commands.Mp.Lock.{nameof(LockedMatch)}");

	/// <summary>Reply after unlocking the room.</summary>
	public static readonly string UnlockedMatch = LocaleCatalog.Get($"Commands.Mp.Lock.{nameof(UnlockedMatch)}");

	/// <summary>Usage line for <c>!mp size</c>.</summary>
	public static readonly string SizeUsage = LocaleCatalog.Get($"Commands.Mp.Lock.{nameof(SizeUsage)}");

	/// <summary>Reply after resizing the room; <c>{0}</c> is the new slot count.</summary>
	public static readonly string ChangedMatchSize = LocaleCatalog.Get($"Commands.Mp.Lock.{nameof(ChangedMatchSize)}");

	// ── !mp move / host / clearhost ──────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp move</c>.</summary>
	public static readonly string MoveUsage = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(MoveUsage)}");

	/// <summary>Reply when the target cannot be resolved to a seated player.</summary>
	public static readonly string UserNotInMatchOrUnregistered =
		LocaleCatalog.Get($"Commands.Mp.Move.{nameof(UserNotInMatchOrUnregistered)}");

	/// <summary>Reply when the destination slot is occupied or otherwise not open.</summary>
	public static readonly string DestinationSlotNotOpen =
		LocaleCatalog.Get($"Commands.Mp.Move.{nameof(DestinationSlotNotOpen)}");

	/// <summary>Reply when the target holds no slot in the match; <c>{0}</c> is the target's name.</summary>
	public static readonly string NotInThisMatch = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(NotInThisMatch)}");

	/// <summary>Reply after moving a player; <c>{0}</c> is the player's name, <c>{1}</c> the destination slot.</summary>
	public static readonly string MovedToSlot = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(MovedToSlot)}");

	/// <summary>Usage line for <c>!mp host</c>.</summary>
	public static readonly string HostUsage = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(HostUsage)}");

	/// <summary>Reply after transferring host; <c>{0}</c> is the new host's name.</summary>
	public static readonly string ChangedMatchHost = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(ChangedMatchHost)}");

	/// <summary>Reply after clearing the host.</summary>
	public static readonly string ClearedMatchHost = LocaleCatalog.Get($"Commands.Mp.Move.{nameof(ClearedMatchHost)}");

	// ── !mp name / password / private ────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp name</c>.</summary>
	public static readonly string NameUsage = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(NameUsage)}");

	/// <summary>Reply after renaming the room; <c>{0}</c> is the new name.</summary>
	public static readonly string RoomNameUpdated = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(RoomNameUpdated)}");

	/// <summary>Reply after clearing the room password.</summary>
	public static readonly string RemovedMatchPassword =
		LocaleCatalog.Get($"Commands.Mp.Name.{nameof(RemovedMatchPassword)}");

	/// <summary>Reply after changing the room password.</summary>
	public static readonly string ChangedMatchPassword =
		LocaleCatalog.Get($"Commands.Mp.Name.{nameof(ChangedMatchPassword)}");

	/// <summary>Reply reporting the room's privacy; <c>{0}</c> is <c>private</c> or <c>not private</c>.</summary>
	public static readonly string
		MatchIsPrivateNow = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(MatchIsPrivateNow)}");

	/// <summary>Reply after making the room private.</summary>
	public static readonly string MatchNowPrivate = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(MatchNowPrivate)}");

	/// <summary>Reply after making the room public.</summary>
	public static readonly string MatchNowPublic = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(MatchNowPublic)}");

	/// <summary>Usage line for <c>!mp private</c>.</summary>
	public static readonly string PrivateUsage = LocaleCatalog.Get($"Commands.Mp.Name.{nameof(PrivateUsage)}");

	// ── !mp invite ───────────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp invite</c>.</summary>
	public static readonly string InviteUsage = LocaleCatalog.Get($"Commands.Mp.Invite.{nameof(InviteUsage)}");

	/// <summary>Reply when the target is not connected with the osu! client.</summary>
	public static readonly string InviteRequiresClient =
		LocaleCatalog.Get($"Commands.Mp.Invite.{nameof(InviteRequiresClient)}");

	/// <summary>Reply when the target is already seated in the room.</summary>
	public static readonly string UserAlreadyInRoom =
		LocaleCatalog.Get($"Commands.Mp.Invite.{nameof(UserAlreadyInRoom)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotInviteBot = LocaleCatalog.Get($"Commands.Mp.Invite.{nameof(CannotInviteBot)}");

	/// <summary>Reply after inviting a player; <c>{0}</c> is the invitee's name.</summary>
	public static readonly string InvitedToRoom = LocaleCatalog.Get($"Commands.Mp.Invite.{nameof(InvitedToRoom)}");

	// ── !mp addref / removeref / listrefs ────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp addref</c>.</summary>
	public static readonly string AddRefUsage = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(AddRefUsage)}");

	/// <summary>Reply when the target cannot be resolved to any user.</summary>
	public static readonly string UserNotFound = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(UserNotFound)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotAddBotReferee =
		LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(CannotAddBotReferee)}");

	/// <summary>Reply after adding a referee; <c>{0}</c> is the new referee's name.</summary>
	public static readonly string AddedReferee = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(AddedReferee)}");

	/// <summary>Reply when the target already holds referee status; <c>{0}</c> is the target.</summary>
	public static readonly string TargetIsAlreadyAReferee =
		LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(TargetIsAlreadyAReferee)}");

	/// <summary>Usage line for <c>!mp removeref</c>.</summary>
	public static readonly string RemoveRefUsage = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(RemoveRefUsage)}");

	/// <summary>Reply when removing the referee would leave the room without one; <c>{0}</c> is the target.</summary>
	public static readonly string CannotRemoveLastReferee =
		LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(CannotRemoveLastReferee)}");

	/// <summary>Reply when the target holds no referee status; <c>{0}</c> is the target.</summary>
	public static readonly string TargetIsNotAReferee =
		LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(TargetIsNotAReferee)}");

	/// <summary>Reply when the target is the match's creator; <c>{0}</c> is the target.</summary>
	public static readonly string CannotRemoveCreator =
		LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(CannotRemoveCreator)}");

	/// <summary>Reply after removing a referee; <c>{0}</c> is the removed referee's name.</summary>
	public static readonly string RemovedReferee = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(RemovedReferee)}");

	/// <summary>Reply when the match has no referees.</summary>
	public static readonly string NoReferees = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(NoReferees)}");

	/// <summary>Header line before the list of referees.</summary>
	public static readonly string MatchReferees = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(MatchReferees)}");

	/// <summary>Reply when the match has no banned players.</summary>
	public static readonly string NoBannedPlayers = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(NoBannedPlayers)}");

	/// <summary>Header line before the list of banned players.</summary>
	public static readonly string MatchBans = LocaleCatalog.Get($"Commands.Mp.Referee.{nameof(MatchBans)}");

	// ── !mp team / set ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp team</c> (id form).</summary>
	public static readonly string TeamUsage = LocaleCatalog.Get($"Commands.Mp.Team.{nameof(TeamUsage)}");

	/// <summary>Usage line for <c>!mp team</c> (name form).</summary>
	public static readonly string TeamUsageName = LocaleCatalog.Get($"Commands.Mp.Team.{nameof(TeamUsageName)}");

	/// <summary>Reply after assigning a team; <c>{0}</c> is the player's name, <c>{1}</c> the team label.</summary>
	public static readonly string MovedToTeam = LocaleCatalog.Get($"Commands.Mp.Team.{nameof(MovedToTeam)}");

	/// <summary>Usage line for <c>!mp set</c>.</summary>
	public static readonly string SetUsage = LocaleCatalog.Get($"Commands.Mp.Team.{nameof(SetUsage)}");

	/// <summary>
	///     Reply after changing settings; <c>{0}</c> is the team type, <c>{1}</c> the win condition, <c>{2}</c> a size
	///     suffix.
	/// </summary>
	public static readonly string ChangedMatchSettings =
		LocaleCatalog.Get($"Commands.Mp.Team.{nameof(ChangedMatchSettings)}");

	// ── !mp map / mods ───────────────────────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp map</c>.</summary>
	public static readonly string MapUsage = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(MapUsage)}");

	/// <summary>Reply when no beatmap carries the requested id; <c>{0}</c> is the id.</summary>
	public static readonly string NoBeatmapWithId = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(NoBeatmapWithId)}");

	/// <summary>Reply after changing the map; <c>{0}</c> artist, <c>{1}</c> title, <c>{2}</c> version.</summary>
	public static readonly string ChangedBeatmap = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(ChangedBeatmap)}");

	/// <summary>Usage line for <c>!mp mods</c>.</summary>
	public static readonly string ModsUsage = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(ModsUsage)}");

	/// <summary>Mod-change summary, enabled case; <c>{0}</c> is the mod list.</summary>
	public static readonly string EnabledMods = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(EnabledMods)}");

	/// <summary>Mod-change summary, disabled case; <c>{0}</c> is the mod list.</summary>
	public static readonly string DisabledMods = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(DisabledMods)}");

	/// <summary>Mod-change summary line when freemod was turned off.</summary>
	public static readonly string DisabledFreemod = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(DisabledFreemod)}");

	/// <summary>Mod-change summary line when freemod was turned on.</summary>
	public static readonly string EnabledFreemod = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(EnabledFreemod)}");

	/// <summary>Mod-change summary when nothing changed.</summary>
	public static readonly string NoModChanges = LocaleCatalog.Get($"Commands.Mp.Map.{nameof(NoModChanges)}");

	// ── !mp start / timer / aborttimer / abort ───────────────────────────────────────────────
	/// <summary>Reply when the match is already in progress.</summary>
	public static readonly string MatchAlreadyInProgress =
		LocaleCatalog.Get($"Commands.Mp.Start.{nameof(MatchAlreadyInProgress)}");

	/// <summary>Reply when a start countdown was queued; <c>{0}</c> is the delay in seconds.</summary>
	public static readonly string MatchStartsInSeconds =
		LocaleCatalog.Get($"Commands.Mp.Start.{nameof(MatchStartsInSeconds)}");

	/// <summary>Reply after starting the match immediately.</summary>
	public static readonly string MatchStarted = LocaleCatalog.Get($"Commands.Mp.Start.{nameof(MatchStarted)}");

	/// <summary>Usage line for <c>!mp timer</c>.</summary>
	public static readonly string TimerUsage = LocaleCatalog.Get($"Commands.Mp.Start.{nameof(TimerUsage)}");

	/// <summary>Reply after starting a countdown; <c>{0}</c> is the duration in seconds.</summary>
	public static readonly string CountdownStarted = LocaleCatalog.Get($"Commands.Mp.Start.{nameof(CountdownStarted)}");

	/// <summary>Reply when no countdown is running.</summary>
	public static readonly string NoCountdownRunning =
		LocaleCatalog.Get($"Commands.Mp.Start.{nameof(NoCountdownRunning)}");

	/// <summary>Reply after aborting a countdown.</summary>
	public static readonly string CountdownAborted = LocaleCatalog.Get($"Commands.Mp.Start.{nameof(CountdownAborted)}");

	/// <summary>Reply when the match is not in progress.</summary>
	public static readonly string MatchNotInProgress =
		LocaleCatalog.Get($"Commands.Mp.Start.{nameof(MatchNotInProgress)}");

	/// <summary>Reply after aborting the match.</summary>
	public static readonly string AbortedMatch = LocaleCatalog.Get($"Commands.Mp.Start.{nameof(AbortedMatch)}");

	// ── !mp kick / ban / unban / close ───────────────────────────────────────────────────────
	/// <summary>Usage line for <c>!mp kick</c>.</summary>
	public static readonly string KickUsage = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(KickUsage)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotKickBot = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(CannotKickBot)}");

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public static readonly string CannotKickReferee =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(CannotKickReferee)}");

	/// <summary>Reply after kicking a player; <c>{0}</c> is the kicked player's name.</summary>
	public static readonly string KickedFromMatch =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(KickedFromMatch)}");

	/// <summary>Usage line for <c>!mp ban</c>.</summary>
	public static readonly string BanUsage = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(BanUsage)}");

	/// <summary>Reply when the target is not a registered user.</summary>
	public static readonly string UserNotRegistered =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(UserNotRegistered)}");

	/// <summary>Reply when the target is BasilBot.</summary>
	public static readonly string CannotBanBot = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(CannotBanBot)}");

	/// <summary>Reply when the target is a referee; <c>{0}</c> is the target's name.</summary>
	public static readonly string CannotBanReferee =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(CannotBanReferee)}");

	/// <summary>Reply after banning a player; <c>{0}</c> is the banned player's name.</summary>
	public static readonly string BannedPlayerFromMatch =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(BannedPlayerFromMatch)}");

	/// <summary>Usage line for <c>!mp unban</c>.</summary>
	public static readonly string UnbanUsage = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(UnbanUsage)}");

	/// <summary>Reply when the target is not banned; <c>{0}</c> is the target's name.</summary>
	public static readonly string NotBannedFromMatch =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(NotBannedFromMatch)}");

	/// <summary>Reply after unbanning a player; <c>{0}</c> is the unbanned player's name.</summary>
	public static readonly string UnbannedFromMatch =
		LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(UnbannedFromMatch)}");

	/// <summary>Reply after closing the match.</summary>
	public static readonly string ClosedMatch = LocaleCatalog.Get($"Commands.Mp.Moderation.{nameof(ClosedMatch)}");

	// ── scope routing (CommandDispatcher) ────────────────────────────────────────────────────
	/// <summary>Reply when a scoped subcommand resolves to no match at all.</summary>
	public static readonly string NotScopedToAnyMatchHint =
		LocaleCatalog.Get($"General.{nameof(NotScopedToAnyMatchHint)}");

	// ── !mp dispatch errors ──────────────────────────────────────────────────────────────────
	/// <summary>Reply when a subcommand name is not recognized; <c>{0}</c> is the subcommand.</summary>
	public static readonly string UnknownMpSubcommand = LocaleCatalog.Get($"General.{nameof(UnknownMpSubcommand)}");

	/// <summary>Reply when a subcommand is limited to the match's creator; <c>{0}</c> is the subcommand.</summary>
	public static readonly string CreatorOnlyMp = LocaleCatalog.Get($"General.{nameof(CreatorOnlyMp)}");

	/// <summary>Reply when a subcommand is not allowed from <c>#lobby</c>; <c>{0}</c> is the subcommand.</summary>
	public static readonly string MpNotUsableFromLobby = LocaleCatalog.Get($"General.{nameof(MpNotUsableFromLobby)}");

	/// <summary>Reply when a chained <c>!mp</c> line is issued from <c>#lobby</c>.</summary>
	public static readonly string MpChainNotUsableFromLobby =
		LocaleCatalog.Get($"General.{nameof(MpChainNotUsableFromLobby)}");

	/// <summary>Reply when <c>!mp in</c> is run from a channel instead of a DM to the bot.</summary>
	public static readonly string MpInDmOnly = LocaleCatalog.Get($"General.{nameof(MpInDmOnly)}");

	// ── !mp chain rejection ──────────────────────────────────────────────────────────────────
	/// <summary>
	///     Reply rejecting a chain segment that is not an <c>!mp</c> command; <c>{0}</c> is the prefix, <c>{1}</c> the
	///     rejected text.
	/// </summary>
	public static readonly string ChainMustBeMp = LocaleCatalog.Get($"General.{nameof(ChainMustBeMp)}");

	/// <summary>
	///     Reply rejecting a chain segment that cannot be chained; <c>{0}</c> is the prefix, <c>{1}</c> the subcommand,
	///     <c>{2}</c> the rejected text.
	/// </summary>
	public static readonly string CannotChainMp = LocaleCatalog.Get($"General.{nameof(CannotChainMp)}");
}