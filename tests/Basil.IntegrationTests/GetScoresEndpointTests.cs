using System.Net;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Ported from app/api/domains/osu.py's getScores, reduced to a status-only reply — per-beatmap
///     leaderboard browsing is out of scope (see BanchoHostGroups.cs's route doc comment), but the
///     map's real RankedStatus is still reported via <see cref="StubMapRepository" />. Covers the
///     auth gate, the mode/mods status-broadcast side effect (this is the only request osu! sends on
///     every song-select map change), and the two status outcomes (known/unknown map).
/// </summary>
public class GetScoresEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GetScoresEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Basil:Server:Domain"] = "test.local",
                    ["Basil:Bot:CommandPrefix"] = "!",
                    ["Basil:Server:MenuIconPath"] = "icon.png",
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IChannelRepository, NullChannelRepository>();
                services.AddSingleton<IUserRepository, StubUserRepository>();
                services.AddSingleton<IPasswordHasher, StubPasswordHasher>();
                services.AddSingleton<IScoreRepository, StubScoreRepository>();
                services.AddSingleton<IMapRepository, StubMapRepository>();
            });
        });
    }

    private static HttpRequestMessage MakeRequest(string queryString)
    {
        return new HttpRequestMessage(HttpMethod.Get, $"/web/osu-osz2-getscores.php?{queryString}")
            { Headers = { Host = "osu.test.local" } };
    }

    [Fact]
    public async Task PlayerNotOnline_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = MakeRequest("us=nobody&ha=x&m=0&mods=0");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongPassword_ReturnsUnauthorized()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(50, "cmyui-wrongpw", "tok", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        var request = MakeRequest("us=cmyui-wrongpw&ha=wrong-md5&m=0&mods=0");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_UnknownMap_ReturnsNotSubmitted()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(51, "cmyui-stub", "tok2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        var request = MakeRequest("us=cmyui-stub&ha=correct-md5&c=unknown-md5&m=0&mods=0");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("-1|false", body);
    }

    [Fact]
    public async Task Authenticated_KnownMap_ReturnsMapsetRankedStatus()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(56, "cmyui-known", "tok5", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        var request = MakeRequest($"us=cmyui-known&ha=correct-md5&c={StubMapRepository.KnownMd5}&m=0&mods=0");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal($"{(int)RankedStatus.Loved}|false", body);
    }

    [Fact]
    public async Task ModeOrModsChanged_BroadcastsUpdatedStatsToOtherSessions()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var player = new PlayerSession(52, "cmyui-status", "tok3", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var other = new PlayerSession(53, "other", "other-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        sessionRegistry.Add(player);
        sessionRegistry.Add(other);
        var request = MakeRequest("us=cmyui-status&ha=correct-md5&m=1&mods=8"); // Taiko + Hidden, differs from defaults

        await _factory.CreateClient().SendAsync(request);

        Assert.NotEmpty(other.Dequeue());
    }

    [Fact]
    public async Task ModeAndModsUnchanged_DoesNotBroadcast()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        var player = new PlayerSession(54, "cmyui-nochange", "tok4", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var other = new PlayerSession(55, "other2", "other-token2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        sessionRegistry.Add(player);
        sessionRegistry.Add(other);
        var request = MakeRequest("us=cmyui-nochange&ha=correct-md5&m=0&mods=0"); // matches PlayerStatus defaults

        await _factory.CreateClient().SendAsync(request);

        Assert.Empty(other.Dequeue());
    }

    private sealed class NullChannelRepository : IChannelRepository
    {
        public Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Channel>>([]);
        }

        public Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Channel?>(null);
        }
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("stored-hash");
        }

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<User>>([]);
        }
    }

    private sealed class StubPasswordHasher : IPasswordHasher
    {
        public string Hash(byte[] passwordMd5)
        {
            throw new NotSupportedException();
        }

        public bool Verify(byte[] untrustedPasswordMd5, string trustedBcryptHash)
        {
            return Encoding.UTF8.GetString(untrustedPasswordMd5) == "correct-md5";
        }
    }

    private sealed class StubMapRepository : IMapRepository
    {
        public const string KnownMd5 = "known-md5";

        private static readonly Mapset Mapset = new(1, "Artist", "Title", "Creator", DateTime.UnixEpoch, DateTime.UnixEpoch);

        private static readonly Beatmap Beatmap = new(
            KnownMd5, 1, Mapset, "Normal", "map.osu", TimeSpan.Zero, 0, false, 0, 0,
            new Difficulty(GameMode.Standard, 0, 0, 0, 0, 0, 0));

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(md5 == KnownMd5 ? Beatmap : null);
        }

        public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode, int offset,
            int amount, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }

    private sealed class StubScoreRepository : IScoreRepository
    {
        public Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FirstPlaceScoreRow?>(null);
        }

        public Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ScoreOwnerRow?>(null);
        }

        public Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundScoreRow>>([]);
        }

        public Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
