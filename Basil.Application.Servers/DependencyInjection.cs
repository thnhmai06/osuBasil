using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Rulesets.Services;
using Basil.Application.Servers.Objects;
using Basil.Application.Servers.Ports;
using Basil.Application.Servers.Services;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Servers;

public static class DependencyInjection
{
	public static IServiceCollection AddServersApplication(this IServiceCollection services)
	{
		services.AddSingleton<ServerContext>(sp => new ServerContext(
			sp.GetRequiredService<SessionContext>(),
			sp.GetRequiredService<SessionMaintenance>(),
			sp.GetRequiredService<BotSessions>(),
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
		services.AddSingleton<UpdateStatus>(sp => new UpdateStatus(
			sp.GetRequiredService<IReleases>(),
			sp.GetRequiredService<IRulesetUpdater>(),
			sp.GetRequiredService<ILogger<UpdateStatus>>(),
			sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<IUpdateStatus>(sp => sp.GetRequiredService<UpdateStatus>());
		return services;
	}
}