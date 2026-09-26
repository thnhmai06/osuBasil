namespace Basil.Application.Services.Operations.Replies;

/// <summary>
///     The localization keys for the <c>!mp</c> command surface, the single source of truth for
///     that surface.
/// </summary>
/// <remarks>
///     Every reply <c>!mp</c> sends is resolved from a named member here (the non-<c>!mp</c>
///     commands have their own holder, <see cref="BotReplies" />) through
///     <see cref="Ports.ILocalizer" />, so production code and tests reference the same symbols and
///     cannot drift on the key. The wording itself is Infrastructure's concern; changing it is a
///     public-behavior change made there, not here.
/// </remarks>
public static class MpReplies
{
	// ── !mp make / makeprivate / join / close ───────────────────────────────────────────────
	public const string CreateFailed = "Commands.Mp.Make.CreateFailed";
	public const string CreatedMatch = "Commands.Mp.Make.CreatedMatch";
	public const string JoinUsage = "Commands.Mp.Url.JoinUsage";
	public const string NoActiveMatchWithId = "Commands.Mp.Url.NoActiveMatchWithId";
	public const string PrivateRoomJoinDenied = "Commands.Mp.Url.PrivateRoomJoinDenied";
	public const string BannedFromMatch = "Commands.Mp.Url.BannedFromMatch";
	public const string JoinedMatch = "Commands.Mp.Url.JoinedMatch";
	public const string IncorrectPassword = "Commands.Mp.Url.IncorrectPassword";
	public const string MatchIsFull = "Commands.Mp.Url.MatchIsFull";
	public const string ClosedMatch = "Commands.Mp.Moderation.ClosedMatch";

	// ── routing / permission errors ─────────────────────────────────────────────────────────
	public const string NotInARoom = "General.NotInARoom";
	public const string UnknownMpSubcommand = "General.UnknownMpSubcommand";
	public const string NotARefereeOfMatch = "Commands.Mp.In.NotARefereeOfMatch";
	public const string CreatorOnlyMp = "General.CreatorOnlyMp";

	// ── !mp settings ─────────────────────────────────────────────────────────────────────────
	public const string SettingsRoomName = "Commands.Mp.Settings.SettingsRoomName";
	public const string SettingsBeatmap = "Commands.Mp.Settings.SettingsBeatmap";
	public const string SettingsBeatmapNotFound = "Commands.Mp.Settings.SettingsBeatmapNotFound";
	public const string SettingsBeatmapNotSelected = "Commands.Mp.Settings.SettingsBeatmapNotSelected";
	public const string SettingsTeamMode = "Commands.Mp.Settings.SettingsTeamMode";
	public const string SettingsActiveMods = "Commands.Mp.Settings.SettingsActiveMods";
	public const string SettingsCreator = "Commands.Mp.Settings.SettingsCreator";
	public const string SettingsPlayers = "Commands.Mp.Settings.SettingsPlayers";

	// ── !mp lock / unlock / size ─────────────────────────────────────────────────────────────
	public const string LockedMatch = "Commands.Mp.Lock.LockedMatch";
	public const string UnlockedMatch = "Commands.Mp.Lock.UnlockedMatch";
	public const string SizeUsage = "Commands.Mp.Lock.SizeUsage";
	public const string ChangedMatchSize = "Commands.Mp.Lock.ChangedMatchSize";

	// ── !mp move / host / clearhost ──────────────────────────────────────────────────────────
	public const string MoveUsage = "Commands.Mp.Move.MoveUsage";
	public const string UserNotInMatchOrUnregistered = "Commands.Mp.Move.UserNotInMatchOrUnregistered";
	public const string DestinationSlotNotOpen = "Commands.Mp.Move.DestinationSlotNotOpen";
	public const string MovedToSlot = "Commands.Mp.Move.MovedToSlot";
	public const string HostUsage = "Commands.Mp.Move.HostUsage";
	public const string ChangedMatchHost = "Commands.Mp.Move.ChangedMatchHost";
	public const string ClearedMatchHost = "Commands.Mp.Move.ClearedMatchHost";

	// ── !mp name / password / private ────────────────────────────────────────────────────────
	public const string NameUsage = "Commands.Mp.Name.NameUsage";
	public const string RoomNameUpdated = "Commands.Mp.Name.RoomNameUpdated";
	public const string RemovedMatchPassword = "Commands.Mp.Name.RemovedMatchPassword";
	public const string ChangedMatchPassword = "Commands.Mp.Name.ChangedMatchPassword";
	public const string MatchIsPrivateNow = "Commands.Mp.Name.MatchIsPrivateNow";
	public const string MatchNowPrivate = "Commands.Mp.Name.MatchNowPrivate";
	public const string MatchNowPublic = "Commands.Mp.Name.MatchNowPublic";
	public const string PrivateUsage = "Commands.Mp.Name.PrivateUsage";

	// ── !mp invite ───────────────────────────────────────────────────────────────────────────
	public const string InviteUsage = "Commands.Mp.Invite.InviteUsage";
	public const string InviteRequiresClient = "Commands.Mp.Invite.InviteRequiresClient";
	public const string UserAlreadyInRoom = "Commands.Mp.Invite.UserAlreadyInRoom";
	public const string InvitedToRoom = "Commands.Mp.Invite.InvitedToRoom";

	// ── !mp addref / removeref / listrefs / banlist ─────────────────────────────────────────
	public const string AddRefUsage = "Commands.Mp.Referee.AddRefUsage";
	public const string UserNotFound = "Commands.Mp.Referee.UserNotFound";
	public const string AddedReferee = "Commands.Mp.Referee.AddedReferee";
	public const string TargetIsAlreadyAReferee = "Commands.Mp.Referee.TargetIsAlreadyAReferee";
	public const string RemoveRefUsage = "Commands.Mp.Referee.RemoveRefUsage";
	public const string TargetIsNotAReferee = "Commands.Mp.Referee.TargetIsNotAReferee";
	public const string CannotRemoveCreator = "Commands.Mp.Referee.CannotRemoveCreator";
	public const string RemovedReferee = "Commands.Mp.Referee.RemovedReferee";
	public const string NoReferees = "Commands.Mp.Referee.NoReferees";
	public const string MatchReferees = "Commands.Mp.Referee.MatchReferees";
	public const string NoBannedPlayers = "Commands.Mp.Referee.NoBannedPlayers";
	public const string MatchBans = "Commands.Mp.Referee.MatchBans";

	// ── !mp team / set ───────────────────────────────────────────────────────────────────────
	public const string TeamUsage = "Commands.Mp.Team.TeamUsage";
	public const string MovedToTeam = "Commands.Mp.Team.MovedToTeam";
	public const string SetUsage = "Commands.Mp.Team.SetUsage";
	public const string ChangedMatchSettings = "Commands.Mp.Team.ChangedMatchSettings";

	// ── !mp map / mods ───────────────────────────────────────────────────────────────────────
	public const string MapUsage = "Commands.Mp.Map.MapUsage";
	public const string NoBeatmapWithId = "Commands.Mp.Map.NoBeatmapWithId";
	public const string ChangedBeatmap = "Commands.Mp.Map.ChangedBeatmap";
	public const string ModsUsage = "Commands.Mp.Map.ModsUsage";
	public const string EnabledMods = "Commands.Mp.Map.EnabledMods";
	public const string DisabledFreemod = "Commands.Mp.Map.DisabledFreemod";
	public const string EnabledFreemod = "Commands.Mp.Map.EnabledFreemod";

	// ── !mp start / timer / aborttimer / abort ───────────────────────────────────────────────
	public const string MatchAlreadyInProgress = "Commands.Mp.Start.MatchAlreadyInProgress";
	public const string MatchStartsInSeconds = "Commands.Mp.Start.MatchStartsInSeconds";
	public const string MatchStarted = "Commands.Mp.Start.MatchStarted";
	public const string TimerUsage = "Commands.Mp.Start.TimerUsage";
	public const string CountdownStarted = "Commands.Mp.Start.CountdownStarted";
	public const string NoCountdownRunning = "Commands.Mp.Start.NoCountdownRunning";
	public const string CountdownAborted = "Commands.Mp.Start.CountdownAborted";
	public const string MatchNotInProgress = "Commands.Mp.Start.MatchNotInProgress";
	public const string AbortedMatch = "Commands.Mp.Start.AbortedMatch";

	// ── !mp kick / ban / unban ────────────────────────────────────────────────────────────────
	public const string KickUsage = "Commands.Mp.Moderation.KickUsage";
	public const string CannotKickReferee = "Commands.Mp.Moderation.CannotKickReferee";
	public const string KickedFromMatch = "Commands.Mp.Moderation.KickedFromMatch";
	public const string BanUsage = "Commands.Mp.Moderation.BanUsage";
	public const string UserNotRegistered = "Commands.Mp.Moderation.UserNotRegistered";
	public const string CannotBanReferee = "Commands.Mp.Moderation.CannotBanReferee";
	public const string BannedPlayerFromMatch = "Commands.Mp.Moderation.BannedPlayerFromMatch";
	public const string UnbanUsage = "Commands.Mp.Moderation.UnbanUsage";
	public const string NotBannedFromMatch = "Commands.Mp.Moderation.NotBannedFromMatch";
	public const string UnbannedFromMatch = "Commands.Mp.Moderation.UnbannedFromMatch";
}