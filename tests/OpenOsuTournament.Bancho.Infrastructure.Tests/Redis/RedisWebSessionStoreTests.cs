using OpenOsuTournament.Bancho.Infrastructure.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Redis;

/// <summary>Ported from app/repositories/web_sessions.py — session tokens stored in redis with a rolling expiry.</summary>
public class RedisWebSessionStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4").Build();
    private ConnectionMultiplexer _connection = null!;
    private RedisWebSessionStore _store = null!;

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _store = new RedisWebSessionStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _redis.DisposeAsync();
    }

    [Fact]
    public async Task FetchUserId_UnknownToken_ReturnsNull()
    {
        Assert.Null(await _store.FetchUserIdAsync("nonexistent-token"));
    }

    [Fact]
    public async Task CreateAndFetch_ReturnsUserId()
    {
        await _store.CreateAsync("token-abc", 42, TimeSpan.FromDays(30));

        Assert.Equal(42, await _store.FetchUserIdAsync("token-abc"));
    }

    [Fact]
    public async Task Delete_TokenNoLongerResolves()
    {
        await _store.CreateAsync("token-abc", 42, TimeSpan.FromDays(30));

        await _store.DeleteAsync("token-abc");

        Assert.Null(await _store.FetchUserIdAsync("token-abc"));
    }

    [Fact]
    public async Task Create_UsesBanchoKeyNamespace()
    {
        await _store.CreateAsync("token-abc", 42, TimeSpan.FromDays(30));

        var db = _connection.GetDatabase();
        Assert.True(await db.KeyExistsAsync("bancho:web_sessions:token-abc"));
    }
}