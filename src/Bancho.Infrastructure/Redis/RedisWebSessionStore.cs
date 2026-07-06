using Bancho.Application.Abstractions;
using StackExchange.Redis;

namespace Bancho.Infrastructure.Redis;

/// <inheritdoc cref="IWebSessionStore" />
public sealed class RedisWebSessionStore(IConnectionMultiplexer connection) : IWebSessionStore
{
    private IDatabase Db => connection.GetDatabase();

    private static string Key(string token) => $"bancho:web_sessions:{token}";

    public Task CreateAsync(string token, int userId, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        Db.StringSetAsync(Key(token), userId.ToString(), expiry);

    public async Task<int?> FetchUserIdAsync(string token, CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync(Key(token));
        return value.HasValue ? (int)value : null;
    }

    public Task DeleteAsync(string token, CancellationToken cancellationToken = default) =>
        Db.KeyDeleteAsync(Key(token));
}
