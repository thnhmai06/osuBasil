using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Basil.Application.Configurations;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the new `/faqs` and `/menu/seasonals` routes: public reads, admin-key-gated writes, and the
///     "no silent override" rule shared by both — `POST` only creates a brand-new entry/file (409 if
///     already taken), `PUT` only replaces an existing one (404 if it isn't). Real temp-directory
///     filesystem, no stubs — both resources are pure file storage with no database involvement.
/// </summary>
public class FaqSeasonalEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private const string AdminKey = "correct-key";
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-faq-seasonal-tests-").FullName;
	private readonly WebApplicationFactory<Program> _factory;

	public FaqSeasonalEndpointTests(WebApplicationFactory<Program> factory)
	{
		_factory = factory.WithWebHostBuilder(builder =>
		{
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Basil:Server:Domain"] = "test.local",
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
				services.AddSingleton(Options.Create(new StorageOptions
				{
					ReplaysPath = Path.Combine(_dataDir, "Replays"),
					AvatarsPath = Path.Combine(_dataDir, "Avatars"),
					MapsetsPath = Path.Combine(_dataDir, "Mapsets"),
					MenuSeasonalsPath = Path.Combine(_dataDir, "Seasonals"),
					MenuBannersPath = Path.Combine(_dataDir, "Banners"),
					FaqsPath = Path.Combine(_dataDir, "Faqs"),
					CachePath = Path.Combine(_dataDir, "Cache")
				}));
			});
		});
	}

	private string FaqsDir => Path.Combine(_dataDir, "Faqs");
	private string SeasonalsDir => Path.Combine(_dataDir, "Seasonals");

	public void Dispose()
	{
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	// ---- /faqs ----

	[Fact]
	public async Task GetFaqList_NoEntries_ReturnsEmptyArray()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<string[]>>();

		response.EnsureSuccessStatusCode();
		Assert.Empty(body!.Data!);
	}

	[Fact]
	public async Task GetFaqList_ReturnsSortedEntryNames()
	{
		Directory.CreateDirectory(FaqsDir);
		await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "rules");
		await File.WriteAllTextAsync(Path.Combine(FaqsDir, "faq.txt"), "faq");

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/faqs/"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<string[]>>();

		Assert.Equal(["faq", "rules"], body!.Data!);
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
		request.Content = new MultipartFormDataContent
			{ { new ByteArrayContent([.. "hi"u8]), "file", "rules.txt" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PostFaq_NewEntry_CreatesFileAndReturnsCreated()
	{
		var file = new ByteArrayContent([.. "hello"u8])
			{ Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } };
		var request = MakeRequest(HttpMethod.Post, "/faqs/", AdminKey);
		request.Content = new MultipartFormDataContent { { file, "file", "rules.txt" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.True(File.Exists(Path.Combine(FaqsDir, "rules.txt")));
	}

	[Fact]
	public async Task PostFaq_AlreadyExists_ReturnsConflict()
	{
		Directory.CreateDirectory(FaqsDir);
		await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "original");

		var file = new ByteArrayContent([.. "new"u8])
			{ Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } };
		var request = MakeRequest(HttpMethod.Post, "/faqs/", AdminKey);
		request.Content = new MultipartFormDataContent { { file, "file", "rules.txt" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(FaqsDir, "rules.txt")));
	}

	[Fact]
	public async Task PutFaq_NotFound_ReturnsNotFound()
	{
		var request = MakeRequest(HttpMethod.Put, "/faqs/nonexistent", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([.. "new"u8]), "file", "x.txt" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PutFaq_Existing_ReplacesContent()
	{
		Directory.CreateDirectory(FaqsDir);
		await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "old");

		var request = MakeRequest(HttpMethod.Put, "/faqs/rules", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([.. "new"u8]), "file", "x.txt" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(FaqsDir, "rules.txt")));
	}

	[Fact]
	public async Task DeleteFaq_NotFound_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Delete, "/faqs/nonexistent", AdminKey));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DeleteFaq_Existing_RemovesFile()
	{
		Directory.CreateDirectory(FaqsDir);
		await File.WriteAllTextAsync(Path.Combine(FaqsDir, "rules.txt"), "content");

		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/faqs/rules", AdminKey));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.False(File.Exists(Path.Combine(FaqsDir, "rules.txt")));
	}

	// ---- /menu/seasonals ----

	[Fact]
	public async Task GetSeasonalList_RedirectsToAssetsHost()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menu/seasonals"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("https://assets.test.local/menu/seasonals", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task GetSeasonalFile_RedirectsToAssetsHost()
	{
		var response =
			await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menu/seasonals/winter.png"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("https://assets.test.local/menu/seasonals/winter.png", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task PostSeasonal_New_CreatesFile()
	{
		var request = MakeRequest(HttpMethod.Post, "/menu/seasonals", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.True(File.Exists(Path.Combine(SeasonalsDir, "spring.png")));
	}

	[Fact]
	public async Task PostSeasonal_AlreadyExists_ReturnsConflict()
	{
		Directory.CreateDirectory(SeasonalsDir);
		await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [9, 9, 9]);

		var request = MakeRequest(HttpMethod.Post, "/menu/seasonals", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
		Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png")));
	}

	[Fact]
	public async Task PutSeasonal_NotFound_ReturnsNotFound()
	{
		var request = MakeRequest(HttpMethod.Put, "/menu/seasonals/nope.png", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "nope.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PutSeasonal_Existing_ReplacesBytes()
	{
		Directory.CreateDirectory(SeasonalsDir);
		await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [9, 9, 9]);

		var request = MakeRequest(HttpMethod.Put, "/menu/seasonals/spring.png", AdminKey);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "spring.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png")));
	}

	[Fact]
	public async Task DeleteSeasonal_NotFound_ReturnsNotFound()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Delete, "/menu/seasonals/nope.png", AdminKey));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DeleteSeasonal_Existing_RemovesFile()
	{
		Directory.CreateDirectory(SeasonalsDir);
		await File.WriteAllBytesAsync(Path.Combine(SeasonalsDir, "spring.png"), [1, 2, 3]);

		var response = await _factory.CreateClient()
			.SendAsync(MakeRequest(HttpMethod.Delete, "/menu/seasonals/spring.png", AdminKey));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.False(File.Exists(Path.Combine(SeasonalsDir, "spring.png")));
	}
}