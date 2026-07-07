using Bancho.Application.Abstractions.Users;
namespace Bancho.Application.Configuration;

/// <summary>
/// Ports REDIS_HOST/REDIS_PORT/REDIS_USER/REDIS_PASS/REDIS_DB from app/settings.py.
/// Building the actual StackExchange.Redis ConfigurationOptions belongs in Infrastructure.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public required string Host { get; init; }
    public int Port { get; init; } = 6379;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public int Database { get; init; }
}
