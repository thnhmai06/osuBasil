using Basil.Application.Spectating;

namespace Basil.Infrastructure.Spectating;

/// <summary>Registers the Spectating slice's services.</summary>
public static class SpectatingServiceCollectionExtensions
{
	/// <summary>Registers the Spectating slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddSpectating(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSpectatingApplication();

		return services;
	}
}