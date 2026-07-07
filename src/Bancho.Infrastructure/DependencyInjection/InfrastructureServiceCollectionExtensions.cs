using Bancho.Application.Abstractions;
using Bancho.Application.Configuration;
using Bancho.Application.Sessions;
using Bancho.Infrastructure.Persistence;
using Bancho.Infrastructure.Redis;
using Bancho.Infrastructure.Security;
using Bancho.Infrastructure.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Bancho.Infrastructure.DependencyInjection;

/// <summary>
/// Composition root helper for the Infrastructure layer: binds Options, builds the MySQL
/// connection string and Redis multiplexer, and registers every port implementation. Repos
/// only need the connection string (each appends its own TreatTinyAsBoolean flag where needed).
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBanchoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RegistrationOptions>(configuration.GetSection(RegistrationOptions.SectionName));
        services.Configure<DiscordOptions>(configuration.GetSection(DiscordOptions.SectionName));
        services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        static string BuildConnectionString(IServiceProvider sp) =>
            DatabaseConnectionStringBuilder.Build(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redis = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = new ConfigurationOptions
            {
                EndPoints = { { redis.Host, redis.Port } },
                DefaultDatabase = redis.Database,
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
        services.AddSingleton<IRelationshipRepository>(sp => new MySqlRelationshipRepository(BuildConnectionString(sp)));
        services.AddSingleton<IMapRepository>(sp => new MySqlMapRepository(BuildConnectionString(sp)));
        services.AddSingleton<IRatingRepository>(sp => new MySqlRatingRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreRepository>(sp => new MySqlScoreRepository(BuildConnectionString(sp)));
        services.AddSingleton<IScoreSubmissionPersistence>(sp => new MySqlScoreSubmissionPersistence(BuildConnectionString(sp)));
        services.AddSingleton<ILogRepository>(sp => new MySqlLogRepository(BuildConnectionString(sp)));

        services.AddSingleton<ILeaderboardStore, RedisLeaderboardStore>();
        services.AddSingleton<IWebSessionStore, RedisWebSessionStore>();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
        services.AddSingleton<IReplayStorage, Storage.FileSystemReplayStorage>();
        services.AddSingleton<ITokenGenerator, GuidTokenGenerator>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton<IPlayerSessionRegistry, InMemoryPlayerSessionRegistry>();
        services.AddSingleton<IChannelRegistry, InMemoryChannelRegistry>();
        services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();

        return services;
    }
}
