using Basil.Server.Features.Multiplayer.Packets;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Multiplayer;

/// <summary>Registers the Multiplayer slice's services and endpoints.</summary>
public static class MultiplayerServiceCollectionExtensions
{
	/// <summary>Registers the Multiplayer slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddMultiplayer(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<MatchBroadcast>();
		services.AddSingleton<MatchMembership>();
		services.AddSingleton<MatchLifecycle>();
		services.AddSingleton<MatchControlService>();
		services.AddSingleton<MatchReportService>();
		services.AddSingleton<MatchRecoveryService>();

		services.AddSingleton<IMatchRepository>(sp =>
			new SqliteMatchRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteMatchRepository>>()));
		services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();
		services.AddSingleton<IMatchLiveEvents, MatchLiveEvents>();

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

		services.AddSingleton<MatchRoundEndOutbox>();
		services.AddSingleton<IMatchRoundEndOutbox>(sp => sp.GetRequiredService<MatchRoundEndOutbox>());
		services.AddHostedService(sp => sp.GetRequiredService<MatchRoundEndOutbox>());

		return services;
	}

	/// <summary>Maps the Multiplayer slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapMultiplayerRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}