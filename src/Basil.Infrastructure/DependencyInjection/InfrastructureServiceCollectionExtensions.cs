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
using Basil.Infrastructure.Performance;
using Basil.Infrastructure.Persistence;
using Basil.Infrastructure.Persistence.Repositories;
using Basil.Infrastructure.Redis;
using Basil.Infrastructure.Security;
using Basil.Infrastructure.Sessions;
using Basil.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Basil.Infrastructure.DependencyInjection;

/// <summary>
///     Composition root helper for the Infrastructure layer: binds Options, builds the MySQL
///     connection string and Redis multiplexer, and registers every port implementation.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBanchoInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.SectionName));
        services.Configure<DiscordOptions>(configuration.GetSection(DiscordOptions.SectionName));
        services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<AdminApiOptions>(configuration.GetSection(AdminApiOptions.SectionName));
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));

        static string BuildConnectionString(IServiceProvider sp)
        {
            return DatabaseConnectionStringBuilder.Build(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);
        }

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redis = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = new ConfigurationOptions
            {
                EndPoints = { { redis.Host, redis.Port } },
                DefaultDatabase = redis.Database
            };
            if (!string.IsNullOrEmpty(redis.User)) config.User = redis.User;
            if (!string.IsNullOrEmpty(redis.Password)) config.Password = redis.Password;
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddSingleton<IUserRepository>(sp => new MySqlUserRepository(BuildConnectionString(sp)));
        services.AddSingleton<IStatsRepository>(sp => new MySqlStatsRepository(BuildConnectionString(sp)));
        services.AddSingleton<IClientHashRepository>(sp => new MySqlClientHashRepository(BuildConnectionString(sp)));
        services.AddSingleton<IIngameLoginRepository>(sp => new MySqlIngameLoginRepository(BuildConnectionString(sp)));
        services.AddSingleton<IChannelRepository>(sp => new MySqlChannelRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMailRepository>(sp => new MySqlMailRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRelationshipRepository>(sp =>
            new MySqlRelationshipRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapRepository>(sp => new MySqlMapRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapsetRepository>(sp => new MySqlMapsetRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRatingRepository>(sp => new MySqlRatingRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreRepository>(sp => new MySqlScoreRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreSubmissionPersistence>(sp =>
            new MySqlScoreSubmissionPersistence(BuildConnectionString(sp)));
        services.AddSingleton<ILogRepository>(sp => new MySqlLogRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMatchPersistenceRepository>(sp =>
            new MySqlMatchPersistenceRepository(BuildConnectionString(sp)));

        services.AddSingleton<ILeaderboardStore, RedisLeaderboardStore>();
        services.AddSingleton<IWebSessionStore, RedisWebSessionStore>();

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

        return services;
    }
}