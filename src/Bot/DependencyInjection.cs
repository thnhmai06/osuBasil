using Basil.Bot.Chat;
using Basil.Bot.Commands;
using Basil.Bot.Commands.Mp;
using Basil.Bot.Replies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Bot;

/// <summary>Registers the BasilBot application's services.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the services BasilBot needs.</summary>
	/// <remarks>The event handlers of the bot are registered by the host together with every other handler.</remarks>
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
		services.AddSingleton<ChannelCommands>();
		services.AddSingleton<ChatCommands>();
		services.AddSingleton<CommandWorker>();
		services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
		return services;
	}
}