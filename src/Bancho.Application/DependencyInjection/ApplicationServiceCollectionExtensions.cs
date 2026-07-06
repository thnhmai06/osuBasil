using Bancho.Application.BackgroundServices;
using Bancho.Application.PacketHandlers;
using Bancho.Application.UseCases.Authentication;
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

        services.AddSingleton<IBanchoPacketHandler, PingHandler>();
        services.AddSingleton<IBanchoPacketHandler, LogoutHandler>();
        services.AddSingleton<IBanchoPacketHandler, ChangeActionHandler>();
        services.AddSingleton<IBanchoPacketHandler, RequestStatusUpdateHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserStatsRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestHandler>();
        services.AddSingleton<IBanchoPacketHandler, UserPresenceRequestAllHandler>();
        services.AddSingleton<IBanchoPacketHandler, ReceiveUpdatesHandler>();
        services.AddSingleton<IBanchoPacketHandler, SetAwayMessageHandler>();

        services.AddSingleton<BanchoPacketDispatcher>();

        services.AddHostedService<GhostDisconnectService>();

        return services;
    }
}
