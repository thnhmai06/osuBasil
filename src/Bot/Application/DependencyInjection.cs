using Basil.Bot.Application.Announcements;
using Basil.Bot.Application.Chat;
using Basil.Bot.Application.Commands;
using Basil.Bot.Application.Commands.Mp;
using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Bot.Application;

/// <summary>Registers the BasilBot application's services.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the services BasilBot needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBotApplication(this IServiceCollection services)
	{
		services.AddSingleton(Random.Shared);
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<MpScopes>();
		services.AddSingleton<MpCommands>();
		services.AddSingleton<ChatCommands>();
		services.AddSingleton<CommandWorker>();
		services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
		services.AddSingleton<IEventHandler<ChannelEvent>, ChatHandler>();
		services.AddSingleton<RoomAnnouncer>();
		services.AddSingleton<IEventHandler<RoomEvent>>(sp => sp.GetRequiredService<RoomAnnouncer>());
		services.AddSingleton<IEventHandler<LobbyEvent>>(sp => sp.GetRequiredService<RoomAnnouncer>());
		services.AddSingleton<IEventHandler<AnticheatEvent>, AnticheatAnnouncer>();
		return services;
	}
}