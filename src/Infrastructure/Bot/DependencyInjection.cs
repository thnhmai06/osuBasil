using Basil.Application.Bot.Replies;
using Basil.Infrastructure.Bot.Handlers.Commands;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;
using Basil.Infrastructure.Bot.Services;
using Basil.Infrastructure.Bot.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot;

/// <summary>Registers the implementations of BasilBot's ports.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the commands, the replies catalog and the workers BasilBot needs.</summary>
	/// <remarks>The event handlers of the bot are registered by the host together with every other handler.</remarks>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBotInfrastructure(this IServiceCollection services)
	{
		services.AddOptions<RepliesOptions>();
		services.AddSingleton<ReplyCatalog>();
		services.AddSingleton<IReplies>(sp => sp.GetRequiredService<ReplyCatalog>());
		services.AddSingleton<RepliesWatcher>();
		services.AddHostedService(sp => sp.GetRequiredService<RepliesWatcher>());
		services.AddSingleton<RoomCountdowns>();
		services.AddSingleton<MpCommands>();
		services.AddSingleton<ChannelCommands>();
		services.AddSingleton<ChatCommands>();
		services.AddSingleton<CommandWorker>();
		services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
		return services;
	}

	/// <summary>Reads where BasilBot's replies file is from <c>Basil:Bot:Replies</c>.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <param name="configuration">The configuration to read.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBotInfrastructureOptions(this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddOptions<RepliesOptions>().Bind(configuration.GetSection("Basil:Bot:Replies"))
			// A relative path is relative to the executable.
			.PostConfigure(replies => replies.Path = Path.IsPathRooted(replies.Path)
				? replies.Path
				: Path.Combine(AppContext.BaseDirectory, replies.Path));
		return services;
	}
}