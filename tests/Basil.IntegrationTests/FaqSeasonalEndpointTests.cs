using System.Net;
using System.Net.Http.Json;
using Basil.Application.Configuration;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new `/faqs` and `/seasonals` routes: public reads, admin-key-gated writes, and the
///     "no silent override" rule shared by both — `POST` only creates a brand-new entry/file (409 if
///     already taken), `PUT` only replaces an existing one (404 if it isn't). Real temp-directory
///     filesystem, no stubs — both resources are pure file storage with no database involvement.
/// </summary>
public class FaqSeasonalEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string AdminKey = "correct-key";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-faq-seasonal-tests-").FullName;

    public FaqSeasonalEndpointTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
    {
        var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
        if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
        return request;
    }

    private string FaqsDir => Path.Combine(_dataDir, "Faqs");
    private string SeasonalsDir => Path.Combine(_dataDir, "Seasonals");

    // ---- /faqs ----

    [Fact]
    public async Task GetFaqList_NoEntries_ReturnsEmptyArray()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/"));
        var body = await response.Content.ReadFromJsonAsync<string[]>();

        response.EnsureSuccessStatusCode();
        Assert.Empty(body!);
    }

    [Fact]
    public async Task GetFaqList_ReturnsSortedEntryNames()
    {
        Directory.CreateDirectory(FaqsDir);
        await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "rules");
        await File.WriteAllTextAsync(Path.Combine(FaqsDir, "faq.txt"), "faq");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/"));
        var body = await response.Content.ReadFromJsonAsync<string[]>();

        Assert.Equal(["faq", "rules"], body);
    }

    [Fact]
    public async Task GetFaqEntry_UnknownEntry_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/nonexistent"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFaqEntry_KnownEntry_ReturnsContent()
    {
        Directory.CreateDirectory(FaqsDir);
        await File.WriteAllLinesAsync(Path.Combine(FaqsDir, "rules.txt"), ["Line one", "Line two"]);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/rules"));
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("Line one\nLine two", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task PostFaq_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
    {
        var request = MakeRequest(HttpMethod.Post, "/faqs/", adminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent("hi"u8.ToArray()), "file", "rules.txt" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostFaq_NewEntry_CreatesFileAndReturnsNoContent()
    {
        var request = MakeRequest(HttpMethod.Post, "/faqs/", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "rules.txt" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(FaqsDir, "rules.txt")));
    }

    [Fact]
    public async Task PostFaq_AlreadyExists_ReturnsConflict()
    {
        Directory.CreateDirectory(FaqsDir);
        await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "original");

        var request = MakeRequest(HttpMethod.Post, "/faqs/", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent("new"u8.ToArray()), "file", "rules.txt" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(FaqsDir, "rules.txt")));
    }

    [Fact]
    public async Task PutFaq_NotFound_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Put, "/faqs/nonexistent", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent("new"u8.ToArray()), "file", "x.txt" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutFaq_Existing_ReplacesContent()
    {
        Directory.CreateDirectory(FaqsDir);
        await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "old");

        var request = MakeRequest(HttpMethod.Put, "/faqs/rules", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent("new"u8.ToArray()), "file", "x.txt" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(FaqsDir, "rules.txt")));
    }

    [Fact]
    public async Task DeleteFaq_NotFound_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/faqs/nonexistent", AdminKey));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFaq_Existing_RemovesFile()
    {
        Directory.CreateDirectory(FaqsDir);
        await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "content");

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/faqs/rules", AdminKey));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(FaqsDir, "rules.txt")));
    }

    // ---- /seasonals ----

    [Fact]
    public async Task GetSeasonalList_ReturnsFileNames()
    {
        Directory.CreateDirectory(SeasonalsDir);
        await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "winter.png"), [1, 2, 3]);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/seasonals/"));
        var body = await response.Content.ReadFromJsonAsync<string[]>();

        Assert.Equal(["winter.png"], body);
    }

    [Fact]
    public async Task GetSeasonalFile_UnknownFileName_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/seasonals/nope.png"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSeasonalFile_Known_ReturnsBytesWithCorrectMimeType()
    {
        Directory.CreateDirectory(SeasonalsDir);
        await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "winter.png"), [1, 2, 3, 4]);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/seasonals/winter.png"));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
    }

    [Fact]
    public async Task PostSeasonal_New_CreatesFile()
    {
        var request = MakeRequest(HttpMethod.Post, "/seasonals/", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(SeasonalsDir, "spring.png")));
    }

    [Fact]
    public async Task PostSeasonal_AlreadyExists_ReturnsConflict()
    {
        Directory.CreateDirectory(SeasonalsDir);
        await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [9, 9, 9]);

        var request = MakeRequest(HttpMethod.Post, "/seasonals/", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png")));
    }

    [Fact]
    public async Task PutSeasonal_NotFound_ReturnsNotFound()
    {
        var request = MakeRequest(HttpMethod.Put, "/seasonals/nope.png", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "nope.png" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutSeasonal_Existing_ReplacesBytes()
    {
        Directory.CreateDirectory(SeasonalsDir);
        await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [9, 9, 9]);

        var request = MakeRequest(HttpMethod.Put, "/seasonals/spring.png", AdminKey);
        request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png")));
    }

    [Fact]
    public async Task DeleteSeasonal_NotFound_ReturnsNotFound()
    {
        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/seasonals/nope.png", AdminKey));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSeasonal_Existing_RemovesFile()
    {
        Directory.CreateDirectory(SeasonalsDir);
        await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [1, 2, 3]);

        var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/seasonals/spring.png", AdminKey));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(SeasonalsDir, "spring.png")));
    }
}
