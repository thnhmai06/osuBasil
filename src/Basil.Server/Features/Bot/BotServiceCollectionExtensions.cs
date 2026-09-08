using Basil.Server.Shared.Configuration;

namespace Basil.Server.Features.Bot;

/// <summary>Registers the Bot slice's services.</summary>
public static class BotServiceCollectionExtensions
{
	/// <summary>Registers the Bot slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddBot(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));

		services.AddSingleton<BotBootstrapService>();
		services.AddSingleton<MpCommandService>();
		services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

		return services;
	}
}
