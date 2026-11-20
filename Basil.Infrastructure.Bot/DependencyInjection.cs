using Basil.Application.Bot.Replies;
using Basil.Infrastructure.Bot.Handlers.Commands;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;
using Basil.Infrastructure.Bot.Services;
using Basil.Infrastructure.Bot.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddBotInfrastructure()
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

		public IServiceCollection AddBotInfrastructureOptions(IConfiguration configuration)
		{
			services.AddOptions<RepliesOptions>().Bind(configuration.GetSection("Basil:Bot:Replies"))
				// A relative path is relative to the executable.
				.PostConfigure(replies => replies.Path = Path.IsPathRooted(replies.Path)
					? replies.Path
					: Path.Combine(AppContext.BaseDirectory, replies.Path));
			return services;
		}
	}
}