using Bancho.Application.BackgroundServices;
using Bancho.Application.PacketHandlers.Channels;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.PacketHandlers.Multiplayer;
using Bancho.Application.PacketHandlers.Spectating;
using Bancho.Application.Sessions;
using Bancho.Application.Sessions.Channels;
using Bancho.Application.UseCases.Anticheat;
using Bancho.Application.UseCases.Authentication;
using Bancho.Application.UseCases.Beatmaps;
using Bancho.Application.UseCases.Mail;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Application.UseCases.Scores;
using Bancho.Application.UseCases.Spectating;
using Microsoft.Extensions.DependencyInjection;

namespace Bancho.Application.DependencyInjection;

/// <summary>
///     Composition root helper for the Application layer: registers use cases, packet handlers, and
///     the dispatcher. Assumes the ports consumed here (repositories, registries, etc.) are already
///     registered by Bancho.Infrastructure's own extension.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBanchoApplication(this IServiceCollection services)
    {
        services.AddSingleton<OsuLoginUseCase>();
        services.AddSingleton<BanchoAuthenticationService>();
        services.AddSingleton<PlayerLogoutService>();
        services.AddSingleton<EnsureBeatmapUseCase>();
        services.AddSingleton<BeatmapLeaderboardService>();
        services.AddSingleton<DirectSearchService>();
        services.AddSingleton<ScoreSubmissionUseCase>();
        services.AddSingleton<ReplayService>();
        services.AddSingleton<ChannelMembershipService>();
        services.AddSingleton<SpectatorService>();
        services.AddSingleton<MatchMembershipService>();
        services.AddSingleton<BeatmapInfoService>();
        services.AddSingleton<ClientIntegrityService>();
        services.AddSingleton<MailReadService>();

        services.AddSingleton<IBanchoPacketHandler, PingHandler>();
        services.AddSingleton<IBanchoPacketHandler, LogoutHandler>();
        services.AddSingleton<IBanchoPacketHandler, ChangeActionHandler>();
        services.AddSingleton<IBanchoPacketHandler, RequestStatusUpdateHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserStatsRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestAllHandler>();
        services.AddSingleton<IBanchoPacketHandler, ReceiveUpdatesHandler>();
        services.AddSingleton<IBanchoPacketHandler, SetAwayMessageHandler>();
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