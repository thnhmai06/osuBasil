using Basil.Application.Abstractions;
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
    public static IServiceCollection AddBanchoInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.Configure<IrcOptions>(configuration.GetSection(IrcOptions.SectionName));

        // Storage folders are fixed, not configurable — always the 5 named folders next to the
        // executable. See StorageOptions.
        services.AddSingleton(Options.Create(new StorageOptions
        {
            ReplaysPath = Path.Combine(AppContext.BaseDirectory, "Replays"),
            AvatarsPath = Path.Combine(AppContext.BaseDirectory, "Avatars"),
            MapsetsPath = Path.Combine(AppContext.BaseDirectory, "Mapsets"),
            SeasonalsPath = Path.Combine(AppContext.BaseDirectory, "Seasonals"),
            FaqsPath = Path.Combine(AppContext.BaseDirectory, "Faqs")
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
        services.AddSingleton<IMailRepository>(sp => new SqliteMailRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRelationshipRepository>(sp =>
            new SqliteRelationshipRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapRepository>(sp => new SqliteMapRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapsetRepository>(sp => new SqliteMapsetRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRatingRepository>(sp => new SqliteRatingRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreRepository>(sp => new SqliteScoreRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreSubmissionPersistence>(sp =>
            new SqliteScoreSubmissionPersistence(BuildConnectionString(sp)));
        services.AddSingleton<ILogRepository>(sp => new SqliteLogRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMatchPersistenceRepository>(sp =>
            new SqliteMatchPersistenceRepository(BuildConnectionString(sp)));
        services.AddSingleton<ILeaderboardStore>(sp => new SqliteLeaderboardStore(BuildConnectionString(sp)));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
        services.AddSingleton<IReplayStorage, FileSystemReplayStorage>();
        services.AddSingleton<IBeatmapDifficultyCalculator, PpyBeatmapDifficultyCalculator>();
        services.AddSingleton<BeatmapIngestionService>();
        services.AddSingleton<ITokenGenerator, GuidTokenGenerator>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IPlayerSessionRegistry, InMemoryPlayerSessionRegistry>();
        services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();
        services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();
        services.AddSingleton<IMatchEventBus, InMemoryMatchEventBus>();

        services.AddHostedService<TcpIrcListener>();

        return services;
    }
}
