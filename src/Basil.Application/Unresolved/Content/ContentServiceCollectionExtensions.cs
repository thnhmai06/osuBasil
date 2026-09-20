using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Unresolved.Content;

/// <summary>Registers the Content slice's Application-layer services.</summary>
public static class ContentServiceCollectionExtensions
{
	/// <summary>Registers the Content slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddContentApplication(this IServiceCollection services)
	{
		services.AddSingleton<MotdService>();

		return services;
	}
}