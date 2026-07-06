using Bancho.Application.BackgroundServices;
using Bancho.Application.Commands;
using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Application.UseCases.Beatmaps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bancho.Application.DependencyInjection;

/// <summary>
/// Composition root helper for the Application layer: registers use cases, packet handlers, and
/// the dispatcher. Assumes the ports consumed here (repositories, registries, etc.) are already
/// registered by Bancho.Infrastructure's own extension.
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
        services.AddSingleton<IBanchoPacketHandler, FriendAddHandler>();
        services.AddSingleton<IBanchoPacketHandler, FriendRemoveHandler>();
        services.AddSingleton<IBanchoPacketHandler, ToggleBlockNonFriendDmsHandler>();

        services.AddSingleton<ICommand, HelpCommand>();
        services.AddSingleton<ICommand, RollCommand>();
        services.AddSingleton<ICommand, BlockCommand>();
        services.AddSingleton<ICommand, UnblockCommand>();
        services.AddSingleton<ICommand, ReconnectCommand>();
        services.AddSingleton<ICommand, ChangeNameCommand>();
        services.AddSingleton<ICommand, ApiKeyCommand>();

        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        services.AddSingleton<BanchoPacketDispatcher>();

        services.AddHostedService<GhostDisconnectService>();

        return services;
    }
}
