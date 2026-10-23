using Basil.Bot.Announcements;
using Basil.Bot.Chat;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Replies;
using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Bot;

/// <summary>Registers the BasilBot application's services.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the services BasilBot needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBot(this IServiceCollection services)
	{
		services.AddSingleton(Random.Shared);
		services.TryAddSingleton(TimeProvider.System);
		services.AddOptions<RepliesOptions>();
		services.AddSingleton(_ => ReplyDefinitions.In(typeof(Reply).Assembly).AsEnumerable());
		services.AddSingleton<ReplyCatalog>();
		services.AddSingleton<IReplies>(sp => sp.GetRequiredService<ReplyCatalog>());
		services.AddSingleton<RepliesWatcher>();
		services.AddHostedService(sp => sp.GetRequiredService<RepliesWatcher>());
		services.AddSingleton<MpTargets>();
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