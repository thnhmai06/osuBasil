using Basil.Application.Anticheat;
using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Beatmaps;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Chat;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Common;
using Basil.Application.Common.Events;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Scores;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Servers;
using Basil.Application.Users;
using Basil.Application.Users.Objects.Events;
using Basil.Host.Bancho;
using Basil.Host.Cli;
using Basil.Host.Irc;
using Basil.Host.Rest;
using Basil.Infrastructure.Auth;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Chat;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Content;
using Basil.Infrastructure.Multiplayer;
using Basil.Infrastructure.Scores;
using Basil.Infrastructure.Users;

namespace Basil.Host;

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
		services.AddBeatmapsBackgroundWork();
		services.AddBeatmapMirrorSettings();
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
		services.AddCommonInfrastructure();

		// The foreign keys decide the order of the migrations, not the project references.
		services.AddUsersInfrastructure();
		services.AddAuthInfrastructure();
		services.AddBeatmapsInfrastructure();
		services.AddContentInfrastructure();
		services.AddScoresInfrastructure();
		services.AddMultiplayerInfrastructure();
		services.AddChatInfrastructure();
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
		services.AddAnticheatApplication();
		services.AddBeatmapsApplication();
		services.AddScoresApplication();
		services.AddMultiplayerApplication();
		services.AddChatApplication();
		services.AddServersApplication();
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
		services.AddCommonApplication();
		services.AddBanchoHost();
		services.AddIrcHost();
		services.AddRestHost();
		services.AddCliFeeds();
		services.AddMediator(options =>
		{
			options.ServiceLifetime = ServiceLifetime.Singleton;
			options.GenerateTypesAsInternal = true;
			options.NotificationPublisherType = typeof(QueuedNotificationPublisher);
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
		public async Task<(Uri? SearchEndpoint, Uri? DownloadEndpoint)> GetAsync(
			CancellationToken cancellationToken = default)
		{
			var mirror = (await settings.GetAsync(cancellationToken)).Beatmaps.Mirror;
			return (mirror.SearchEndpoint, mirror.DownloadEndpoint);
		}
	}
}