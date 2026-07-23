using System.Net;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
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
///     Ported from app/api/domains/osu.py's osuSearchHandler/osuSearchSetHandler, replumbed to query
///     the local maps table instead of a mirror. Only wiring (auth gate, query binding, dispatch to
///     the right formatter) is covered here — DirectSearchService/DirectSearchResponseFormatter have
///     their own unit tests.
/// </summary>
public class DirectSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubMapRepository _maps = new();

    public DirectSearchEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IMapRepository>(_maps);
            });
        });
    }

    private static Beatmap MakeBeatmap(int id, int setId)
    {
        var mapset = new Mapset(setId, "Artist", "Title", "cmyui", DateTime.UtcNow, DateTime.UtcNow);
        return new Beatmap(
            new string('0', 32), id, mapset, "Version", "file.osu", TimeSpan.FromSeconds(100), 500, 0, 0,
            new Difficulty(GameMode.Standard, 180, 4, 9, 8, 5, 6.5));
    }

    private static HttpRequestMessage MakeRequest(string path, string queryString)
    {
        return new HttpRequestMessage(HttpMethod.Get, $"{path}?{queryString}")
            { Headers = { Host = "osu.test.local" } };
    }

    [Fact]
    public async Task Search_PlayerNotOnline_ReturnsUnauthorized()
    {
        var request = MakeRequest("/web/osu-search.php", "u=nobody&h=x&r=4&q=Newest&m=-1&p=0");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_Authenticated_ReturnsFormattedResults()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(60, "search-user", "tok", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        _maps.SearchResult = [[MakeBeatmap(1, 100)]];
        var request = MakeRequest("/web/osu-search.php", "u=search-user&h=correct-md5&r=4&q=Newest&m=-1&p=0");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("1\n100.osz|Artist|Title|cmyui|", body);
    }

    [Fact]
    public async Task SearchSet_PlayerNotOnline_ReturnsUnauthorized()
    {
        var request = MakeRequest("/web/osu-search-set.php", "u=nobody&h=x&s=100");

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SearchSet_UnknownSet_ReturnsEmptyBody()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(61, "searchset-unknown", "tok2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        _maps.SetInfo = null;
        var request = MakeRequest("/web/osu-search-set.php", "u=searchset-unknown&h=correct-md5&s=999");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("", body);
    }

    [Fact]
    public async Task SearchSet_KnownSet_ReturnsFormattedSetLine()
    {
        var sessionRegistry = _factory.Services.GetRequiredService<IPlayerSessionRegistry>();
        sessionRegistry.Add(new PlayerSession(62, "searchset-known", "tok3", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
        _maps.SetInfo = MakeBeatmap(1, 100);
        var request = MakeRequest("/web/osu-search-set.php", "u=searchset-known&h=correct-md5&s=100");

        var response = await _factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("100.osz|Artist|Title|cmyui|", body);
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
        public IReadOnlyList<IReadOnlyList<Beatmap>> SearchResult { get; set; } = [];
        public Beatmap? SetInfo { get; set; }

        public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
            int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetInfo);
        }

        public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(beatmap);
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
            string? query, GameMode? mode, int offset, int amount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SearchResult);
        }

        public Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }

        public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }
}