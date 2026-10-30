using Basil.Application.Chat.Persistence;
using Basil.Infrastructure.Chat.Persistence;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Common;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Chat;

/// <summary>Registers the persistence implementations owned by Chat.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Chat persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddChatInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<ChatDbContext>>();
		services.AddSingleton<PostgresChannelRepository>();
		services.AddSingleton<IChannelRepository>(provider => provider.GetRequiredService<PostgresChannelRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresChannelRepository>());
		return services;
	}
}