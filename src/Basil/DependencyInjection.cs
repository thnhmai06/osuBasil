using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Beatmaps;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Bot;
using Basil.Application.Chat;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Diagnostics;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Scores;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Servers;
using Basil.Application.Shared;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users;
using Basil.Application.Users.Objects.Events;
using Basil.Configuration;
using Basil.Gateway;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Cli;
using Basil.Host.Irc;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest;
using Basil.Host.Stable;
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

/// <summary>Registers every module of the server, which the hosts call once.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the server: the modules' application objects and their storage, with the ports to external
	///     computation that the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBasil(this IServiceCollection services)
	{
		services.AddBasilStorage();
		services.AddHostedService<RuntimeStartup>();
		services.AddUsersBackgroundWork();
		services.AddMultiplayerBackgroundWork();
		services.AddBotInfrastructure();
		services.AddBeatmapsBackgroundWork();
		services.AddBeatmapMirrorSettings();
		services.AddGatewayLimits();
		return services.AddBasilApplication();
	}

	/// <summary>
	///     Adds the server's storage: the database with a migration history for each module, the repositories and
	///     the file storages, prepared at startup.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBasilStorage(this IServiceCollection services)
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

	/// <summary>
	///     Adds the modules' application objects and the publishing of events, which use the repository, storage and
	///     capability ports the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBasilApplication(this IServiceCollection services)
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

	/// <summary>Registers the second admission tier, which reads its limits from the server settings.</summary>
	internal static IServiceCollection AddGatewayLimits(this IServiceCollection services)
	{
		services.AddSingleton<SettingsGatewayLimits>();
		services.AddSingleton(sp => new Admission(sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<SettingsGatewayLimits>().Current, sp.GetRequiredService<LoadSignal>()));
		return services;
	}

	/// <summary>Registers the reading of the beatmap mirror endpoints from the server settings.</summary>
	internal static IServiceCollection AddBeatmapMirrorSettings(this IServiceCollection services)
	{
		services.AddSingleton<IBeatmapMirrorSettings, BeatmapMirrorSettingsReader>();
		return services;
	}

	/// <summary>
	///     Registers the publishing of events: every category is published by the one publisher, and the handlers
	///     found in the modules observe the events they name.
	/// </summary>
	internal static IServiceCollection AddEvents(this IServiceCollection services)
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

	private static void AddCategory<T>(IServiceCollection services) where T : Event
	{
		services.AddSingleton<IEventPublisher<T>>(sp => sp.GetRequiredService<EventPublisher>());
	}

	private sealed class BeatmapMirrorSettingsReader(ISettingsRepository settings) : IBeatmapMirrorSettings
	{
		/// <summary>Reads the configured beatmap mirror endpoints, or <see langword="null" /> for one that is not set.</summary>
		public async Task<(Uri? SearchEndpoint, Uri? DownloadEndpoint)> GetAsync(
			CancellationToken ct = default)
		{
			var mirror = (await settings.GetAsync(ct)).Beatmaps.Mirror;
			return (mirror.SearchEndpoint, mirror.DownloadEndpoint);
		}
	}
}