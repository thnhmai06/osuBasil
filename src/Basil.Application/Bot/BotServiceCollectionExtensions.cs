using Basil.Application.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Bot;

/// <summary>Registers the Bot slice's Application-layer services.</summary>
public static class BotServiceCollectionExtensions
{
	/// <summary>Registers the Bot slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddBotApplication(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
		services.AddSingleton<BotBootstrapService>();

		return services;
	}
}