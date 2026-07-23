using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Performance;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Security;
using Basil.Infrastructure.Sessions;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.DependencyInjection;

/// <summary>
///     Composition root helper for the Infrastructure layer: binds Options, builds the SQLite
///     connection string, and registers every port implementation.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.Configure<IrcOptions>(configuration.GetSection(IrcOptions.SectionName));

        // Database path is fixed to Data/Basil.db next to the executable — not configurable.
        services.AddSingleton(Options.Create(new DatabaseOptions()));

        // Storage folders are fixed under a Data/ subdirectory next to the executable.
        services.AddSingleton(Options.Create(new StorageOptions
        {
            ReplaysPath = Path.Combine(AppContext.BaseDirectory, "Data", "Replays"),
            AvatarsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Avatars"),
            MapsetsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Mapsets"),
            SeasonalsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Seasonals"),
            FaqsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Faqs")
        }));

        static string BuildConnectionString(IServiceProvider sp)
        {
            return DatabaseConnectionStringBuilder.Build(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);
        }

        services.AddSingleton<IUserRepository>(sp => new SqliteUserRepository(BuildConnectionString(sp)));
        services.AddSingleton<IStatsRepository>(sp => new SqliteStatsRepository(BuildConnectionString(sp)));
        services.AddSingleton<IClientHashRepository>(sp => new SqliteClientHashRepository(BuildConnectionString(sp)));
        services.AddSingleton<IIngameLoginRepository>(sp =>
            new SqliteIngameLoginRepository(BuildConnectionString(sp)));
        services.AddSingleton<IChannelRepository>(sp => new SqliteChannelRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRelationshipRepository>(sp =>
            new SqliteRelationshipRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapRepository>(sp => new SqliteMapRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapsetRepository>(sp => new SqliteMapsetRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreRepository>(sp => new SqliteScoreRepository(BuildConnectionString(sp)));
        services.AddSingleton<ILogRepository>(sp => new SqliteLogRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMatchPersistenceRepository>(sp =>
            new SqliteMatchPersistenceRepository(BuildConnectionString(sp)));
        services.AddSingleton<ILeaderboardStore>(sp => new SqliteLeaderboardStore(BuildConnectionString(sp)));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
        services.AddSingleton<IReplayStorage, FileSystemReplayStorage>();
        services.AddSingleton<IDifficultyCalculator, PpyDifficultyCalculator>();
        services.AddSingleton<BeatmapIngestionService>();
        services.AddSingleton<ITokenGenerator, GuidTokenGenerator>();

        services.AddSingleton<IPlayerSessionRegistry, InMemoryPlayerSessionRegistry>();
        services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();
        services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();
        services.AddSingleton<IMatchLiveEvents, MatchLiveEvents>();
        services.AddSingleton<IPlayerInputEvents, PlayerInputEvents>();

        services.AddHostedService<TcpIrcListener>();
        services.AddHostedService<BeatmapWatcherService>();
        services.AddHostedService<MapsetGarbageCollectorService>();

        return services;
    }
}
