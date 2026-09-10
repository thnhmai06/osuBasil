using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Shared.Media;
using Basil.Server.Shared.Sessions;
using Basil.Server.Shared.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Server.Host;

/// <summary>Registers the services owned by <c>Shared/</c>, used by more than one slice.</summary>
public static class SharedInfrastructureServiceCollectionExtensions
{
	/// <summary>
	///     Registers every <c>Shared/</c> service into the container: options binding, the SQLite
	///     connection string's dependencies, the packet dispatcher, session registries, the shared
	///     memory cache, and the background services that watch player sessions.
	/// </summary>
	/// <param name="services">The service collection to register into.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same <paramref name="services" /> instance, for chaining.</returns>
	public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services,
		IConfiguration configuration)
	{
		// Database path is fixed to Data/Basil.db next to the executable; not configurable.
		services.AddSingleton(Options.Create(new DatabaseOptions()));

		// Storage folders are fixed under a Data/ subdirectory next to the executable.
		services.AddSingleton(Options.Create(new StorageOptions
		{
			ReplaysPath = Path.Combine(AppContext.BaseDirectory, "Data", "Replays"),
			AvatarsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Avatars"),
			BeatmapsetsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Beatmapsets"),
			MenuSeasonalsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Menu", "Seasonals"),
			MenuBannersPath = Path.Combine(AppContext.BaseDirectory, "Data", "Menu", "Banners"),
			FaqsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Faqs"),
			CachePath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache")
		}));

		// SizeLimit bounds the caching decorators sharing this instance (beatmap/beatmapset/user/settings
		// lookups) to a fixed entry count regardless of catalog growth; each decorator's cache.Set call
		// assigns Size = 1 accordingly.
		services.AddMemoryCache(options => options.SizeLimit = 10_000);

		services.AddSingleton<IResponseCache, FileSystemResponseCache>();
		services.AddSingleton<IAudioExtractor, FfmpegAudioExtractor>();

		services.AddSingleton<ISessionRegistry<GameSession>, GameSessionRegistry>();
		services.AddSingleton<PlayerLogoutService>();

		services.AddSingleton<ILiveEventHub, LiveEventHub>();

		services.AddSingleton<PacketDispatcher>();
		services.AddHostedService<GhostDisconnectService>();

		return services;
	}
}