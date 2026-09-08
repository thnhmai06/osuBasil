using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Auth;

/// <summary>Registers the Auth slice's services.</summary>
public static class AuthServiceCollectionExtensions
{
	/// <summary>Registers the Auth slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<LoginService>();
		services.AddSingleton<AuthenticationService>();
		services.AddSingleton<AdminKeyService>();
		services.AddSingleton<ClientIntegrityService>();

		services.AddSingleton<ILoginRepository>(sp =>
			new SqliteLoginRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteLoginRepository>>()));
		services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
		services.AddSingleton<ITokenGenerator, GuidTokenGenerator>();

		return services;
	}

	/// <summary>Maps the Auth slice's routes onto the `api.` host's `/settings` group.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapAuthRoutes(this RouteGroupBuilder group)
	{
		group.MapGroup("/settings").MapAdminKeyRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}