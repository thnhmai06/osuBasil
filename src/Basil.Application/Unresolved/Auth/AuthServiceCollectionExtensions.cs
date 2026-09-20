using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Unresolved.Auth;

/// <summary>Registers the Auth slice's Application-layer services.</summary>
public static class AuthServiceCollectionExtensions
{
	/// <summary>Registers the Auth slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddAuthApplication(this IServiceCollection services)
	{
		services.AddSingleton<CredentialVerifier>();
		services.AddSingleton<AuthenticationService>();
		services.AddSingleton<AdminKeyService>();
		services.AddSingleton<ClientIntegrityService>();

		return services;
	}
}