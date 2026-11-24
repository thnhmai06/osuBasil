using Basil.Application.Bot.Replies;
using Basil.Infrastructure.Bot.Handlers.Commands;
using Basil.Infrastructure.Bot.Handlers.Commands.Mp;
using Basil.Infrastructure.Bot.Services;
using Basil.Infrastructure.Bot.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Bot;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddBotInfrastructure()
		{
			services.AddSingleton<ReplyCatalog>();
			services.AddSingleton<IReplies>(sp => sp.GetRequiredService<ReplyCatalog>());
			services.AddSingleton<RepliesWatcher>();
			services.AddHostedService(sp => sp.GetRequiredService<RepliesWatcher>());
			services.AddSingleton<RoomCountdowns>();
			services.AddSingleton<RoomAnnouncements>();
			services.AddSingleton<MpCommands>();
			services.AddSingleton<ChannelCommands>();
			services.AddSingleton<ChatCommands>();
			services.AddSingleton<CommandWorker>();
			services.AddHostedService(sp => sp.GetRequiredService<CommandWorker>());
			return services;
		}
	}
}