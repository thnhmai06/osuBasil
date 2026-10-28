using Basil.Application.Anticheat.Runtime;
using Basil.Application.Chat.Runtime;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Bot.Announcements;
using Basil.Bot.Chat;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Replies;
using Mediator;
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
		services.AddSingleton<INotificationHandler<ChannelMessagePosted>, ChatHandler>();
		services.AddSingleton<INotificationHandler<AnticheatPlayerFlagged>, AnticheatAnnouncer>();
		services.AddSingleton<RoomAnnouncer>();
		AddRoomAnnouncer<LobbyRoomClosingAnnounced>(services);
		AddRoomAnnouncer<LobbyRoomClosed>(services);
		AddRoomAnnouncer<RoomRoundStarted>(services);
		AddRoomAnnouncer<RoomRoundAborted>(services);
		AddRoomAnnouncer<RoomRoundDecided>(services);
		AddRoomAnnouncer<RoomRoundEnded>(services);
		AddRoomAnnouncer<RoomCountdownStarted>(services);
		AddRoomAnnouncer<RoomCountdownTicked>(services);
		AddRoomAnnouncer<RoomCountdownElapsed>(services);
		AddRoomAnnouncer<RoomCountdownCancelled>(services);
		AddRoomAnnouncer<RoomPlayerJoined>(services);
		return services;
	}

	private static void AddRoomAnnouncer<TEvent>(IServiceCollection services) where TEvent : INotification
	{
		services.AddSingleton<INotificationHandler<TEvent>>(sp =>
			(INotificationHandler<TEvent>)(object)sp.GetRequiredService<RoomAnnouncer>());
	}
}