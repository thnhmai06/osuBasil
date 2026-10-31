using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
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