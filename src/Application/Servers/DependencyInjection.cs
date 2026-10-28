using Basil.Application.Beatmaps.Runtime;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Runtime;
using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Servers.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Servers;

/// <summary>Registers the runtime objects owned by Servers.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Servers runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddServersApplication(this IServiceCollection services)
	{
		services.AddSingleton<ServerContext>(sp => new ServerContext(
			sp.GetRequiredService<ConnectionContext>(),
			sp.GetRequiredService<ChannelContext>(),
			sp.GetRequiredService<RoomChannels>(),
			sp.GetRequiredService<PmChannels>(),
			sp.GetRequiredService<IScreenshotInspector>(),
			sp.GetRequiredService<IScreenshotStorage>(),
			sp.GetRequiredService<Lobby>(),
			sp.GetRequiredService<IChannelRepository>(),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<Beatmapsets>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<IFaqStorage>(),
			sp.GetRequiredService<IMenuBannerRepository>(),
			sp.GetRequiredService<IMenuBannerStorage>(),
			sp.GetRequiredService<IMenuSeasonalsStorage>(),
			sp.GetRequiredService<IMenuIconStorage>()));
		services.AddSingleton<Server>(sp => new Server(sp.GetRequiredService<ServerContext>()));
		return services;
	}
}