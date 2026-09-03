using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Content;
using Basil.Application.Abstractions.Login;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Cache;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Media;
using Basil.Infrastructure.Performance;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Security;
using Basil.Infrastructure.Sessions;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure;

/// <summary>
///     The Infrastructure layer's composition root: binds Options, builds the SQLite connection
///     string, and registers every port implementation.
/// </summary>
public static class DependencyInjection
{
	/// <summary>
	///     Registers every Infrastructure service into the container: the port implementations
	///     (SQLite repositories, caching decorators, media processors, the osu!lazer calculator, the
	///     password hasher and score decryptor, session registries, and storage providers) plus the
	///     background services that watch the mapsets folder and reclaim folders marked for deletion.
	/// </summary>
	/// <param name="services">The service collection to register into.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same <paramref name="services" /> instance, for chaining.</returns>
	public static IServiceCollection AddInfrastructure(this IServiceCollection services,
		IConfiguration configuration)
	{
		services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));
		services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
		services.Configure<IrcOptions>(configuration.GetSection(IrcOptions.SectionName));

		// Database path is fixed to Data/Basil.db next to the executable; not configurable.
		services.AddSingleton(Options.Create(new DatabaseOptions()));

		// Storage folders are fixed under a Data/ subdirectory next to the executable.
		services.AddSingleton(Options.Create(new StorageOptions
		{
			ReplaysPath = Path.Combine(AppContext.BaseDirectory, "Data", "Replays"),
			AvatarsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Avatars"),
			MapsetsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Mapsets"),
			MenuSeasonalsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Menu", "Seasonals"),
			MenuBannersPath = Path.Combine(AppContext.BaseDirectory, "Data", "Menu", "Banners"),
			FaqsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Faqs"),
			CachePath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache")
		}));

		// SizeLimit bounds the caching decorators sharing this instance (beatmap/beatmapset/user/settings
		// lookups) to a fixed entry count regardless of catalog growth; each decorator's cache.Set call
		// assigns Size = 1 accordingly.
		services.AddMemoryCache(options => options.SizeLimit = 10_000);

		services.AddSingleton<IUserRepository>(sp =>
			new CachingUserRepository(
				new SqliteUserRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteUserRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingUserRepository>>()));
		services.AddSingleton<IUserStatRepository>(sp => new SqliteUserStatRepository(BuildConnectionString(sp)));
		services.AddSingleton<IClientHashRepository>(sp =>
			new SqliteClientHashRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteClientHashRepository>>()));
		services.AddSingleton<ILoginRepository>(sp =>
			new SqliteLoginRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteLoginRepository>>()));
		services.AddSingleton<IChannelRepository>(sp => new SqliteChannelRepository(BuildConnectionString(sp)));
		services.AddSingleton<IRelationshipRepository>(sp =>
			new SqliteRelationshipRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteRelationshipRepository>>()));
		services.AddSingleton<IBeatmapRepository>(sp =>
			new CachingBeatmapRepository(
				new SqliteBeatmapRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteBeatmapRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingBeatmapRepository>>()));
		services.AddSingleton<IBeatmapsetRepository>(sp =>
			new CachingBeatmapsetRepository(
				new SqliteBeatmapsetRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteBeatmapsetRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingBeatmapsetRepository>>()));
		services.AddSingleton<IScoreRepository>(sp =>
			new SqliteScoreRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteScoreRepository>>()));
		services.AddSingleton<IUserLogRepository>(sp =>
			new SqliteUserLogRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteUserLogRepository>>()));
		services.AddSingleton<IMatchRepository>(sp =>
			new SqliteMatchRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteMatchRepository>>()));
		services.AddSingleton<ILeaderboardStore>(sp => new SqliteLeaderboardStore(BuildConnectionString(sp)));
		services.AddSingleton<ISettingsRepository>(sp =>
			new CachingSettingsRepository(
				new SqliteSettingsRepository(BuildConnectionString(sp)),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingSettingsRepository>>()));
		services.AddSingleton<IMenuBannerRepository>(sp => new SqliteMenuBannerRepository(BuildConnectionString(sp)));

		services.AddHttpClient<IMirrorSearchClient, HttpMirrorSearchClient>();

		services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
		services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
		services.AddSingleton<IReplayStorage, FileSystemReplayStorage>();
		services.AddSingleton<IResponseCache, FileSystemResponseCache>();
		services.AddSingleton<IAudioExtractor, FfmpegAudioExtractor>();
		services.AddSingleton<IOsuCalculator, PpyOsuCalculator>();
		services.AddSingleton<BeatmapIngestionService>();
		services.AddSingleton<ITokenGenerator, GuidTokenGenerator>();

		services.AddSingleton<ISessionRegistry<GameSession>, GameSessionRegistry>();
		services.AddSingleton<ISessionRegistry<IrcSession>, IrcSessionRegistry>();
		services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();
		services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();
		services.AddSingleton<IMatchLiveEvents, MatchLiveEvents>();
		services.AddSingleton<IPlayerInputEvents, PlayerInputEvents>();
		services.AddSingleton<IPlayerStatusEvents, PlayerStatusEvents>();

		services.AddHostedService<TcpIrcListener>();
		services.AddHostedService<BeatmapWatcherService>();
		services.AddHostedService<MapsetGarbageCollectorService>();

		return services;

		static string BuildConnectionString(IServiceProvider sp)
		{
			return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
		}
	}
}