using Basil.Application.Chat;
using Basil.Application.Shared.Configuration;
using Basil.Domain.Channels;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Application.Channels;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Chat;

/// <summary>Registers the Chat slice's services.</summary>
public static class ChatServiceCollectionExtensions
{
	/// <summary>Registers the Chat slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddChat(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddChatApplication();

		services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();

		services.AddSingleton<IChannelRepository>(sp => new SqliteChannelRepository(BuildConnectionString(sp)));

		services.AddHostedService<ChatMetricsPublisher>();

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}