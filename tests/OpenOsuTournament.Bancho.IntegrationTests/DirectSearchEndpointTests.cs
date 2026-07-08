using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Abstractions.Users;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Users;

namespace Bancho.IntegrationTests;

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
                    ["ServerBehavior:Domain"] = "test.local",
                    ["ServerBehavior:CommandPrefix"] = "!",
                    ["ServerBehavior:MenuIconUrl"] = "https://example.test/icon.png",
                    ["ServerBehavior:MenuOnclickUrl"] = "https://example.test"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IChannelRepository, NullChannelRepository>();
                services.AddSingleton<IUserRepository, StubUserRepository>();
                services.AddSingleton<IPasswordHasher, StubPasswordHasher>();
                services.AddSingleton<IMapRepository>(_maps);
            });
        });
    }

    private static Beatmap MakeBeatmap(int id, int setId)
    {
        return new Beatmap(
            new string('0', 32), id, setId, "Artist", "Title", "Version", "cmyui", DateTime.UtcNow, 100, 500,
            RankedStatus.Ranked, false, 0, 0, GameMode.VanillaOsu, 180, 4, 8, 9, 5, 6.5, "file.osu");
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
        sessionRegistry.Add(new PlayerSession(60, "search-user", "tok", Privileges.Unrestricted, 0.0));
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
        sessionRegistry.Add(new PlayerSession(61, "searchset-unknown", "tok2", Privileges.Unrestricted, 0.0));
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
        sessionRegistry.Add(new PlayerSession(62, "searchset-known", "tok3", Privileges.Unrestricted, 0.0));
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

        public Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateApiKeyAsync(int id, string apiKey, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
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
            int? setId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SetInfo);
        }

        public Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
            string? query, GameMode? mode, RankedStatus? status, int offset, int amount,
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

        public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Beatmap>>([]);
        }
    }
}