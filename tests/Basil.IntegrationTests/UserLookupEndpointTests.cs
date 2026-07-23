using System.Net;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers <see cref="Basil.Web.Routing.UserLookup" />: every public `GET /users/{idOrName}...`
///     route accepts a username in place of the numeric id, resolving via
///     <see cref="IUserRepository.FetchByNameAsync" /> and 302-redirecting to the canonical numeric
///     path. A numeric segment is served directly (not redirected); an unknown username 404s.
/// </summary>
public class UserLookupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly StubUserRepository _users = new();

    public UserLookupEndpointTests(WebApplicationFactory<Program> factory)
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
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test",
                    ["Basil:Server:AdminKey"] = "correct-key"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IUserRepository>(_users);
            });
        });
    }

    private HttpClient MakeClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static HttpRequestMessage MakeRequest(string path, string? adminKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    [Fact]
    public async Task GetUser_ByUsername_RedirectsToCanonicalId()
    {
        _users.ByName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

        var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player", "correct-key"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/users/7", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetUser_ByUnknownUsername_ReturnsNotFound()
    {
        var response = await MakeClient().SendAsync(MakeRequest("/users/nobody", "correct-key"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserAvatar_ByUsername_RedirectsToCanonicalId()
    {
        _users.ByName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

        var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player/avatar", "correct-key"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/users/7/avatar", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetUserLive_ByUsername_RedirectsToCanonicalId()
    {
        _users.ByName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

        var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player/live"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/users/7/live", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetUser_NumericId_IsNotRedirected()
    {
        var response = await MakeClient().SendAsync(MakeRequest("/users/999", "correct-key"));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    private sealed class StubUserRepository : IUserRepository
    {
        public Dictionary<string, User> ByName { get; } = [];

        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ByName.Values.FirstOrDefault(u => u.Id == id));
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ByName.GetValueOrDefault(name));
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
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
}
