using System.Net;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Domain.Users;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the avatar verb change: `POST /users/{userId}/avatar` became `PUT`, and a new
///     `DELETE /users/{userId}/avatar` resets a user back to the default avatar by removing every
///     uploaded file for that id (always 204, idempotent whether or not one existed).
/// </summary>
public class AvatarManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-avatar-tests-").FullName;

    public AvatarManagementEndpointTests(WebApplicationFactory<Program> factory)
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
                    ["Basil:Server:AdminKey"] = AdminKey
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
                services.AddSingleton<IUserRepository>(new StubUserRepository());
                services.AddSingleton<IOptions<StorageOptions>>(Options.Create(new StorageOptions
                {
                    ReplaysPath = Path.Combine(_dataDir, "Replays"),
                    AvatarsPath = Path.Combine(_dataDir, "Avatars"),
                    MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
                    SeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
                    FaqsPath = Path.Combine(_dataDir, "Faqs")
                }));
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    private static HttpRequestMessage MakeUploadRequest(HttpMethod method, int userId, string fileName = "avatar.png")
    {
        var request = MakeRequest(method, $"/users/{userId}/avatar");
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", fileName } };
        return request;
    }

    [Fact]
    public async Task PutAvatar_ValidUpload_ReturnsNoContentAndStoresFile()
    {
        var response = await _factory.CreateClient().SendAsync(MakeUploadRequest(HttpMethod.Put, 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(_dataDir, "Avatars", "1.png")));
    }

    [Fact]
    public async Task PutAvatar_MissingAdminKey_ReturnsUnauthorized()
    {
        var request = MakeRequest(HttpMethod.Put, "/users/1/avatar", adminKey: null);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "avatar.png" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAvatar_ExistingUpload_RemovesFileAndReturnsNoContent()
    {
        var client = _factory.CreateClient();
        await client.SendAsync(MakeUploadRequest(HttpMethod.Put, 2));

        var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/users/2/avatar"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_dataDir, "Avatars", "2.png")));
    }

    [Fact]
    public async Task DeleteAvatar_NoAvatarUploaded_StillReturnsNoContent()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/users/999/avatar"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAvatar_MissingAdminKey_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient()
            .SendAsync(MakeRequest(HttpMethod.Delete, "/users/1/avatar", adminKey: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAvatar_NoLongerSupported()
    {
        var response = await _factory.CreateClient().SendAsync(MakeUploadRequest(HttpMethod.Post, 3));

        Assert.False(response.IsSuccessStatusCode);
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
