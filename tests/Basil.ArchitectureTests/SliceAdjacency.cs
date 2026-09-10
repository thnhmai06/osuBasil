namespace Basil.ArchitectureTests;

/// <summary>
///     The cross-slice references this codebase permits, each one a deliberate decision.
/// </summary>
/// <remarks>
///     Slices are not forbidden from referencing each other -- Multiplayer genuinely needs to
///     resolve usernames, Scores genuinely needs to resolve beatmaps -- because a ban would be
///     unenforceable and blanket permission would make the rule meaningless. Every edge is named
///     here instead, so an undeclared one fails the build and a growing list is a visible signal
///     that a boundary is wrong.
/// </remarks>
/// <remarks>
///     This list was derived empirically: starting from an empty allowlist, running
///     <see cref="SliceBoundaryTests.Slices_Should_Only_Reference_Declared_Slices" /> against the
///     real, already-merged <c>Basil.Server</c> assembly, and adding back only the edges the
///     codebase actually exercises. Every edge below names the file(s) that need it.
/// </remarks>
internal static class SliceAdjacency
{
	public static readonly (string From, string To)[] Allowed =
	[
		// LoginService seeds the account's channel membership at login.
		// ClientIntegrityService and LoginService look up the bot's session by
		// BotBootstrapService.BotId to send it a message. Constant-only until the const became a
		// static readonly field, which is why the edge is only now declared.
		("Auth", "Bot"),

		("Auth", "Chat"),

		// AdminKeyService reads/writes ISettingsRepository; LoginService touches
		// MenuIconService/MotdService to build the login-time payload.
		("Auth", "Content"),

		// ClientIntegrityService inspects the caller's IrcSession/IIrcConnection when handling
		// anticheat flags.
		("Auth", "Irc"),

		// ClientIntegrityService resolves the player's current MatchSession/MatchMembershipService
		// when handling anticheat flags.
		("Auth", "Multiplayer"),

		// LoginService publishes the freshly-logged-in player's presence via
		// IPlayerStatusEvents/SpectatorService.
		("Auth", "Spectating"),

		// AuthenticationService/LoginService resolve IUserRepository and peers -- the whole point
		// of authentication.
		("Auth", "Users"),

		// MirrorService reads the configured mirror endpoint from Content's ISettingsRepository.
		("Beatmaps", "Content"),

		// MpCommandService resolves IBeatmapRepository for `!mp map`.
		("Bot", "Beatmaps"),

		// BotBootstrapService, CommandDispatcher and MpCommandService operate on
		// ChannelSession/ChannelMembershipService/IChannelRegistry to post replies and manage
		// channel membership.
		("Bot", "Chat"),

		// CommandDispatcher resolves Content.FaqService for `!faq`.
		("Bot", "Content"),

		// MpCommandService and CommandDispatcher's ScopedDmReplySink reply over Irc.IIrcConnection.
		("Bot", "Irc"),

		// CommandDispatcher/ICommandDispatcher/MpCommandService are the `!mp` command surface --
		// they operate directly on IMatchRegistry, MatchSession and MatchControlService.
		("Bot", "Multiplayer"),

		// BotBootstrapService, CommandDispatcher and MpCommandService resolve IUserRepository to
		// look up command targets.
		("Bot", "Users"),

		// ChatDispatchService routes `!`-prefixed messages to Bot.ICommandDispatcher and replies
		// through ICommandReplySink.
		("Chat", "Bot"),

		// ChannelMembershipService and ChatDispatchService (and its nested reply sinks) bridge to
		// Irc.IrcSession/IIrcConnection/IrcReplies.
		("Chat", "Irc"),

		// ChannelMembershipService, ChatDispatchService and Packets.LobbyJoinHandler operate on
		// match-owned channels (IMatchRegistry, MatchSession, MatchChatMessage, UserBrief).
		("Chat", "Multiplayer"),

		// ChatDispatchService checks IRelationshipRepository (DM blocking) and IUserRepository.
		("Chat", "Users"),

		// MirrorSettingsRoutes (the `/settings/mirror` endpoint) configures Beatmaps.MirrorService.
		("Content", "Beatmaps"),

		// IrcAuthenticationService reuses Auth's IPasswordHasher/ITokenGenerator for IRC login.
		("Irc", "Auth"),

		// IrcAuthenticationService, IrcQueryService, TcpIrcConnection and TcpIrcListener are IRC's
		// client to Chat's channel model (ChannelSession/ChannelMembershipService/IChannelRegistry/
		// ChatDispatchService).
		("Irc", "Chat"),

		// IrcQueryService resolves Content.MotdService for the IRC MOTD command.
		("Irc", "Content"),

		// BanchoIrcBridgeConnection reads MatchSession.ChatChannelName to translate the internal
		// match-channel name into the client-facing "#multiplayer" alias.
		("Irc", "Multiplayer"),

		// IrcAuthenticationService resolves IUserRepository to authenticate an IRC login.
		("Irc", "Users"),

		// Match live/report/routing types resolve IBeatmapRepository, BeatmapDetail and
		// BeatmapsetSummary to describe the map a match is playing.
		("Multiplayer", "Beatmaps"),

		// InMemoryMatchRegistry, MatchMembershipService, MatchSubResourceRoutes and the tourney
		// join/leave packet handlers manage each match's owned chat channel.
		("Multiplayer", "Chat"),

		// MatchControlService, MatchLiveSnapshotBuilder, MatchMembershipService, MatchReportService,
		// MatchRoutes, MatchSubResourceRoutes and UserBriefResolver resolve Irc.IrcSession to report
		// a participant's IRC presence.
		("Multiplayer", "Irc"),

		// MatchReportService writes Scores.ScoreReport/IScoreRepository when a round completes.
		("Multiplayer", "Scores"),

		// MatchLiveRoutes and MatchRoutes hand a joining player's live-input registration off to
		// Spectating.IPlayerInputEvents.
		("Multiplayer", "Spectating"),

		// Nearly every match type resolves IUserRepository to describe its players.
		("Multiplayer", "Users"),

		// ScoreSubmissionService authenticates the submitting player via Auth.AuthenticationService.
		("Scores", "Auth"),

		// ScoreDetailView, ScoreRoutes and ScoreSubmissionService resolve beatmap data for the score
		// they describe.
		("Scores", "Beatmaps"),

		// ScoreRoutes resolves Irc.IrcSession to report the scoring player's online presence.
		("Scores", "Irc"),

		// A score belongs to a match: ScoreDetailView/ScoreRoutes/ScoreSubmissionService resolve
		// UserBrief, MatchSession/MatchSlot and MatchLiveSnapshotBuilder.
		("Scores", "Multiplayer"),

		// ScoreRoutes and ScoreSubmissionService resolve IUserRepository/IUserStatRepository.
		("Scores", "Users"),

		// SpectatorService joins the spectator's #spec_ channel via
		// ChannelMembershipService/IChannelRegistry.
		("Spectating", "Chat"),

		// SpectateEvent/SpectateFramesEvent/SpectateStateEvent/Packets.SpectateFramesHandler carry a
		// Multiplayer.UserBrief describing who is being spectated.
		("Spectating", "Multiplayer"),

		// UserRoutes uses Auth.IPasswordHasher for account creation/password changes.
		// AvatarRoutes and UserRoutes gate admin-only behaviour on AdminKeyDefaults.
		("Users", "Auth"),

		// The per-player live stream is addressed under the user resource
		// (`/users/{id}/live`, UserRoutes) and spectating consumes user presence changes
		// (Packets/ChangeActionHandler.cs).
		("Users", "Spectating"),

		// BeatmapsetRoutes and BeatmapsetAssetRoutes gate admin-only behaviour and private-set
		// visibility on AdminKeyDefaults.
		("Beatmaps", "Auth"),

		// Every Content route gates its write side on AdminKeyDefaults.
		("Content", "Auth"),

		// AnnounceRoutes excludes the bot from an announcement by BotBootstrapService.BotId.
		("Content", "Bot"),

		// MatchRoutes and MatchSubResourceRoutes gate referee-only operations on AdminKeyDefaults.
		("Multiplayer", "Auth"),

		// MatchControlService, TimerHandler and MatchLiveSnapshotBuilder resolve the bot's session by
		// BotBootstrapService.BotId; the bot is the match's default host.
		("Multiplayer", "Bot"),

		// AvatarRoutes and UserRoutes skip the bot account by BotBootstrapService.BotId.
		("Users", "Bot"),

		// DiagnosticRoutes gates every /diagnostic route on AdminKeyDefaults.
		("Diagnostics", "Auth")
	];
}