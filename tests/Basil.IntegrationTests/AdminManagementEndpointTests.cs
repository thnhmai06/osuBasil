using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Domain.Users;
using Basil.Web;
using Basil.Web.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the third slice of Phase C: the admin-key gate on the management REST API. Per the
///     review that shaped this phase, the admin key is the one thing here that MUST be verified —
///     everything else is boilerplate CRUD. This is because a wrong-key DELETE returning 200 instead of 401
///     would be a real, silent security hole on destructive endpoints.
/// </summary>
public class AdminManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminManagementEndpointTests(WebApplicationFactory<Program> factory)
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
                services.AddSingleton<IUserRepository>(new StubUserRepository());
            });
        });
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task DeleteUser_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/user/1", adminKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task DeleteMapset_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/mapset/1", adminKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Block/unblock (POST/DELETE /users/{id}/block/{targetId}) was dropped entirely per the /user
    // redesign — no replacement route exists, so this can no longer succeed.
    [Fact]
    public async Task BlockUser_RouteNoLongerExists()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/user/1/block/2", "correct-key"));

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task GetUsers_MissingAdminKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/user"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_CorrectAdminKey_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/user", "correct-key"));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateUser_InvalidUsername_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var request = MakeRequest(HttpMethod.Post, "/user", "correct-key");
        request.Content = JsonContent.Create(new CreateUserRequest("ab", "hunter2", null, null));

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("between 3 and 15 characters", body);
    }

    [Fact]
    public async Task GetUserAvatar_MissingAdminKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/user/1/avatar"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserAvatar_NoAvatarUploaded_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/user/1/avatar", "correct-key"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_BasilBotId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/user/0", "correct-key"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_BasilBotId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var request = MakeRequest(HttpMethod.Patch, "/user/0", "correct-key");
        request.Content = JsonContent.Create(new UpdateUserRequest("newname", null, null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUserLive_BasilBotId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/user/0/live"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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