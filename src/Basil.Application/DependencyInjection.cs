using Basil.Application.BackgroundServices;
using Basil.Application.PacketHandlers.Channels;
using Basil.Application.PacketHandlers.Core;
using Basil.Application.PacketHandlers.Multiplayer;
using Basil.Application.PacketHandlers.Spectating;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Authentication;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application;

/// <summary>
///     Composition root helper for the Application layer: registers use cases, packet handlers, and
///     the dispatcher. Assumes the ports consumed here (repositories, registries, etc.) are already
///     registered by Basil.Infrastructure's own extension.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<LoginService>();
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<PlayerLogoutService>();
        services.AddSingleton<DirectSearchService>();
        services.AddSingleton<ScoreSubmissionService>();
        services.AddSingleton<ReplayService>();
        services.AddSingleton<ChannelMembershipService>();
        services.AddSingleton<SpectatorService>();
        services.AddSingleton<MatchMembershipService>();
        services.AddSingleton<MatchReportService>();
        services.AddSingleton<MatchRecoveryService>();
        services.AddSingleton<ClientIntegrityService>();
        services.AddSingleton<BotBootstrapService>();
        services.AddSingleton<MpCommandService>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<ChatDispatchService>();
        services.AddSingleton<IrcAuthenticationService>();

        services.AddSingleton<IBanchoPacketHandler, PingHandler>();
        services.AddSingleton<IBanchoPacketHandler, LogoutHandler>();
        services.AddSingleton<IBanchoPacketHandler, ChangeActionHandler>();
        services.AddSingleton<IBanchoPacketHandler, RequestStatusUpdateHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserStatsRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestAllHandler>();
        services.AddSingleton<IBanchoPacketHandler, ReceiveUpdatesHandler>();
        services.AddSingleton<IBanchoPacketHandler, SetAwayMessageHandler>();
        services.AddSingleton<IBanchoPacketHandler, FriendAddHandler>();
        services.AddSingleton<IBanchoPacketHandler, FriendRemoveHandler>();
        services.AddSingleton<IBanchoPacketHandler, ChannelJoinHandler>();
        services.AddSingleton<IBanchoPacketHandler, ChannelPartHandler>();
        services.AddSingleton<IBanchoPacketHandler, SendPublicMessageHandler>();
        services.AddSingleton<IBanchoPacketHandler, SendPrivateMessageHandler>();
        services.AddSingleton<IBanchoPacketHandler, ToggleBlockNonFriendDmsHandler>();
        services.AddSingleton<IBanchoPacketHandler, StartSpectatingHandler>();
        services.AddSingleton<IBanchoPacketHandler, StopSpectatingHandler>();
        services.AddSingleton<IBanchoPacketHandler, SpectateFramesHandler>();
        services.AddSingleton<IBanchoPacketHandler, CantSpectateHandler>();
        services.AddSingleton<IBanchoPacketHandler, CreateMatchHandler>();
        services.AddSingleton<IBanchoPacketHandler, JoinMatchHandler>();
        services.AddSingleton<IBanchoPacketHandler, PartMatchHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchChangeSlotHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchReadyHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchLockHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchChangeSettingsHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchStartHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchChangeModsHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchLoadCompleteHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchNoBeatmapHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchNotReadyHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchFailedHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchHasBeatmapHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchSkipRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchTransferHostHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchChangeTeamHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchChangePasswordHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchScoreUpdateHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchCompleteHandler>();
        services.AddSingleton<IBanchoPacketHandler, MatchInviteHandler>();
        services.AddSingleton<IBanchoPacketHandler, TourneyMatchInfoRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, TourneyMatchJoinChannelHandler>();
        services.AddSingleton<IBanchoPacketHandler, TourneyMatchLeaveChannelHandler>();

        services.AddSingleton<BanchoPacketDispatcher>();

        services.AddHostedService<GhostDisconnectService>();

        return services;
    }
}