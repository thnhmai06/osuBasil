using Basil.Application.Chat.Persistence;
using Basil.Infrastructure.Chat.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Chat;

public static class DependencyInjection
{
	public static IServiceCollection AddChatInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<ChatDbContext>>();
		services.AddSingleton<EfChannelRepository>();
		services.AddSingleton<IChannelRepository>(provider => provider.GetRequiredService<EfChannelRepository>());
		return services;
	}
}