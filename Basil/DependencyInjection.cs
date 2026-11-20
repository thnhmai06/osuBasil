using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Beatmaps;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Bot;
using Basil.Application.Chat;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Diagnostics;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Scores;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Servers;
using Basil.Application.Shared;
using Basil.Application.Shared.Events;
using Basil.Application.Users;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Configuration;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Cli;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest;
using Basil.Host.Stable.Routes;
using Basil.Infrastructure.Anticheat;
using Basil.Infrastructure.Authentication;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Bot;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Content;
using Basil.Infrastructure.Diagnostics;
using Basil.Infrastructure.Diagnostics.Pipeline;
using Basil.Infrastructure.Multiplayer;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Scores;
using Basil.Infrastructure.Servers;
using Basil.Infrastructure.Shared;
using Basil.Infrastructure.Users;
using Basil.Lifetime;

namespace Basil;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddBasil()
		{
			services.AddBasilStorage();
			services.AddHostedService<RuntimeStartup>();
			services.AddUsersBackgroundWork();
			services.AddMultiplayerBackgroundWork();
			services.AddBotInfrastructure();
			services.AddBeatmapsBackgroundWork();
			services.AddScoresBackgroundWork();
			services.AddGatewayLimits();
			return services.AddBasilApplication();
		}

		public IServiceCollection AddBasilStorage()
		{
			services.AddSharedInfrastructure();
			services.AddPersistenceInfrastructure();

			// The foreign keys decide the order of the migrations, not the project references.
			services.AddUsersInfrastructure();
			services.AddAuthInfrastructure();
			services.AddBeatmapsInfrastructure();
			services.AddContentInfrastructure();
			services.AddSharedMigration();
			services.AddScoresInfrastructure();
			services.AddMultiplayerInfrastructure();
			services.AddChatInfrastructure();
			services.AddAnticheatInfrastructure();
			services.AddServersInfrastructure();
			services.AddDiagnosticsInfrastructure();
			return services;
		}

		public IServiceCollection AddBasilApplication()
		{
			services.AddEvents();
			services.AddUsersApplication();
			services.AddBeatmapsApplication();
			services.AddScoresApplication();
			services.AddMultiplayerApplication();
			services.AddChatApplication();
			services.AddBotApplication();
			services.AddServersApplication();
			services.AddDiagnosticsApplication();
			return services;
		}

		internal IServiceCollection AddGatewayLimits()
		{
			services.AddSingleton<SettingsGatewayLimits>();
			services.AddSingleton(sp => new Admission(sp.GetRequiredService<TimeProvider>(),
				sp.GetRequiredService<SettingsGatewayLimits>().Current, sp.GetRequiredService<LoadSignal>()));
			return services;
		}

		internal IServiceCollection AddEvents()
		{
			services.AddSharedApplication();
			services.AddStableHost();
			services.AddIrcHost();
			services.AddRestHost();
			services.AddCliFeeds();
			services.AddMediator(options =>
			{
				options.ServiceLifetime = ServiceLifetime.Singleton;
				options.GenerateTypesAsInternal = true;
				options.NotificationPublisherType = typeof(QueuedNotificationPublisher);
				options.PipelineBehaviors = [typeof(MessageMeasurement<,>)];
			});
			AddCategory<UserEvent>(services);
			AddCategory<ChannelEvent>(services);
			AddCategory<LobbyEvent>(services);
			AddCategory<RoomEvent>(services);
			AddCategory<AnticheatEvent>(services);
			AddCategory<ScoreEvent>(services);
			AddCategory<BeatmapsetEvent>(services);
			return services;
		}
	}

	private static void AddCategory<T>(IServiceCollection services) where T : Event
	{
		services.AddSingleton<IEventPublisher<T>>(sp => sp.GetRequiredService<EventPublisher>());
	}
}