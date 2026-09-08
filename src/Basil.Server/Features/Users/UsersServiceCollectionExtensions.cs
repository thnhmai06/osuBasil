using Basil.Server.Features.Users.Packets;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Users;

/// <summary>Registers the Users slice's services and endpoints.</summary>
public static class UsersServiceCollectionExtensions
{
	/// <summary>Registers the Users slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddUsers(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<IUserRepository>(sp =>
			new CachingUserRepository(
				new SqliteUserRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteUserRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingUserRepository>>()));
		services.AddSingleton<IUserStatRepository>(sp => new SqliteUserStatRepository(BuildConnectionString(sp)));
		services.AddSingleton<IClientHashRepository>(sp =>
			new SqliteClientHashRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteClientHashRepository>>()));
		services.AddSingleton<IRelationshipRepository>(sp =>
			new SqliteRelationshipRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteRelationshipRepository>>()));
		services.AddSingleton<IUserLogRepository>(sp =>
			new SqliteUserLogRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteUserLogRepository>>()));

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

		return services;
	}

	/// <summary>Maps the Users slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapUsersRoutes(this RouteGroupBuilder group)
	{
		group.MapUserRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}
