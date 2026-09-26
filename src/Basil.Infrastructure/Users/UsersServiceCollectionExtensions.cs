using Basil.Application.Shared.Configuration;
using Basil.Application.Social;
using Basil.Application.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Users;

/// <summary>Registers the Users slice's services.</summary>
public static class UsersServiceCollectionExtensions
{
	/// <summary>Registers the Users slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddUsers(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton(sp =>
			new CachingUserRepository(
				new SqliteUserRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteUserRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingUserRepository>>()));
		services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<CachingUserRepository>());
		services.AddSingleton<IUserCache>(sp => sp.GetRequiredService<CachingUserRepository>());
		services.AddSingleton<IUserStatRepository>(sp => new SqliteUserStatRepository(BuildConnectionString(sp)));
		services.AddSingleton<IClientHashRepository>(sp =>
			new SqliteClientHashRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteClientHashRepository>>()));
		services.AddSingleton<IRelationshipRepository>(sp =>
			new SqliteRelationshipRepository(BuildConnectionString(sp),
				sp.GetRequiredService<IUserCache>(),
				sp.GetRequiredService<ILogger<SqliteRelationshipRepository>>()));
		services.AddSingleton<IUserLogRepository>(sp =>
			new SqliteUserLogRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteUserLogRepository>>()));

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}