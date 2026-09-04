using Basil.Application.Abstractions.Bot;
using Basil.Application.Backgrounds;
using Basil.Application.Packets;
using Basil.Application.Packets.Channels;
using Basil.Application.Packets.Multiplayer;
using Basil.Application.Packets.Spectating;
using Basil.Application.Packets.Users;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Content;
using Basil.Application.Services.Irc;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application;

/// <summary>
///     Composition root helper for the Application layer: registers the layer's services with the
///     container.
/// </summary>
/// <remarks>
///     The use cases registered here depend on ports (repositories, registries, and other
///     infrastructure) that are expected to be registered already by Basil.Infrastructure's own
///     extension before this one runs.
/// </remarks>
public static class DependencyInjection
{
	/// <summary>
	///     Registers the Application layer's singletons, packet handlers, dispatcher, and background
	///     services into the given service collection.
	/// </summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.AddSingleton<LoginService>();
		services.AddSingleton<AuthenticationService>();
		services.AddSingleton<AdminKeyService>();
		services.AddSingleton<PlayerLogoutService>();
		services.AddSingleton<DirectSearchService>();
		services.AddSingleton<ScoreSubmissionService>();
		services.AddSingleton<ReplayService>();
		services.AddSingleton<ChannelMembershipService>();
		services.AddSingleton<SpectatorService>();
		services.AddSingleton<MatchMembershipService>();
		services.AddSingleton<MatchControlService>();
		services.AddSingleton<MatchReportService>();
		services.AddSingleton<MatchRecoveryService>();
		services.AddSingleton<ClientIntegrityService>();
		services.AddSingleton<BotBootstrapService>();
		services.AddSingleton<MpCommandService>();
		services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
		services.AddSingleton<ChatDispatchService>();
		services.AddSingleton<IrcAuthenticationService>();
		services.AddSingleton<IrcQueryService>();
		services.AddSingleton<FaqService>();
		services.AddSingleton<MenuSeasonalService>();
		services.AddSingleton<MenuIconService>();
		services.AddSingleton<MenuBannerService>();

		services.AddSingleton<IPacketHandler, PingHandler>();
		services.AddSingleton<IPacketHandler, LogoutHandler>();
		services.AddSingleton<IPacketHandler, ChangeActionHandler>();
		services.AddSingleton<IPacketHandler, RequestStatusUpdateHandler>();
		services.AddSingleton<IPacketHandler, UserStatsRequestHandler>();
		services.AddSingleton<IPacketHandler, UserPresenceRequestHandler>();
		services.AddSingleton<IPacketHandler, UserPresenceRequestAllHandler>();
		services.AddSingleton<IPacketHandler, ReceiveUpdatesHandler>();
		services.AddSingleton<IPacketHandler, SetAwayMessageHandler>();
		services.AddSingleton<IPacketHandler, FriendAddHandler>();
		services.AddSingleton<IPacketHandler, FriendRemoveHandler>();
		services.AddSingleton<IPacketHandler, ChannelJoinHandler>();
		services.AddSingleton<IPacketHandler, ChannelPartHandler>();
		services.AddSingleton<IPacketHandler, LobbyJoinHandler>();
		services.AddSingleton<IPacketHandler, LobbyPartHandler>();
		services.AddSingleton<IPacketHandler, SendPublicMessageHandler>();
		services.AddSingleton<IPacketHandler, SendPrivateMessageHandler>();
		services.AddSingleton<IPacketHandler, ToggleBlockNonFriendDmsHandler>();
		services.AddSingleton<IPacketHandler, StartSpectatingHandler>();
		services.AddSingleton<IPacketHandler, StopSpectatingHandler>();
		services.AddSingleton<IPacketHandler, SpectateFramesHandler>();
		services.AddSingleton<IPacketHandler, CantSpectateHandler>();
		services.AddSingleton<IPacketHandler, CreateMatchHandler>();
		services.AddSingleton<IPacketHandler, JoinMatchHandler>();
		services.AddSingleton<IPacketHandler, PartMatchHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeSlotHandler>();
		services.AddSingleton<IPacketHandler, MatchReadyHandler>();
		services.AddSingleton<IPacketHandler, MatchLockHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeSettingsHandler>();
		services.AddSingleton<IPacketHandler, MatchStartHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeModsHandler>();
		services.AddSingleton<IPacketHandler, MatchLoadCompleteHandler>();
		services.AddSingleton<IPacketHandler, MatchNoBeatmapHandler>();
		services.AddSingleton<IPacketHandler, MatchNotReadyHandler>();
		services.AddSingleton<IPacketHandler, MatchFailedHandler>();
		services.AddSingleton<IPacketHandler, MatchHasBeatmapHandler>();
		services.AddSingleton<IPacketHandler, MatchSkipRequestHandler>();
		services.AddSingleton<IPacketHandler, MatchTransferHostHandler>();
		services.AddSingleton<IPacketHandler, MatchChangeTeamHandler>();
		services.AddSingleton<IPacketHandler, MatchChangePasswordHandler>();
		services.AddSingleton<IPacketHandler, MatchScoreUpdateHandler>();
		services.AddSingleton<IPacketHandler, MatchCompleteHandler>();
		services.AddSingleton<IPacketHandler, MatchInviteHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchInfoRequestHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchJoinChannelHandler>();
		services.AddSingleton<IPacketHandler, TourneyMatchLeaveChannelHandler>();

		services.AddSingleton<PacketDispatcher>();

		services.AddHostedService<GhostDisconnectService>();

		services.AddSingleton<MatchRoundEndOutbox>();
		services.AddSingleton<IMatchRoundEndOutbox>(sp => sp.GetRequiredService<MatchRoundEndOutbox>());
		services.AddHostedService(sp => sp.GetRequiredService<MatchRoundEndOutbox>());

		return services;
	}
}