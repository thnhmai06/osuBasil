using Basil.Bot.Application.Announcements;
using Basil.Bot.Application.Commands;
using Basil.Bot.Application.Commands.Mp;
using Basil.Bot.Application.Replies;
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
		services.AddSingleton<Random>(Random.Shared);
		services.TryAddSingleton<TimeProvider>(TimeProvider.System);
		services.AddSingleton<MpScopes>();
		services.AddSingleton<MpCommands>();
		services.AddSingleton<ChatCommands>();
		services.AddSingleton<ReplyWriter>();
		services.AddSingleton<RoomAnnouncer>();
		services.AddSingleton<AnticheatAnnouncer>();
		services.AddHostedService<BotRunner>();
		return services;
	}
}
