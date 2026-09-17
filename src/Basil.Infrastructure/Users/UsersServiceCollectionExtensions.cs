using Basil.Application.Shared.Configuration;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Application.Users;
using Basil.Application.Social;
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

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}