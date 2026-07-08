using Basil.Application.Abstractions.Users;
using StackExchange.Redis;

namespace Basil.Infrastructure.Redis;

/// <inheritdoc cref="IWebSessionStore" />
public sealed class RedisWebSessionStore(IConnectionMultiplexer connection) : IWebSessionStore
{
    private IDatabase Db => connection.GetDatabase();

    public Task CreateAsync(string token, int userId, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        return Db.StringSetAsync(Key(token), userId.ToString(), expiry);
    }

    public async Task<int?> FetchUserIdAsync(string token, CancellationToken cancellationToken = default)
    {
        var value = await Db.StringGetAsync(Key(token));
        return value.HasValue ? (int)value : null;
    }

    public Task DeleteAsync(string token, CancellationToken cancellationToken = default)
    {
        return Db.KeyDeleteAsync(Key(token));
    }

    private static string Key(string token)
    {
        return $"basil:web_sessions:{token}";
    }
}