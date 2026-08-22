using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Basil.Application.Abstractions.Content;
using Basil.Application.Configurations;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `/menu/banners` (admin CRUD on the `api.` host) and `GET assets.&lt;domain&gt;/menu-content.json`
///     (the public manifest). Backed by a real, stateful <see cref="InMemoryMenuBannerRepository" />
///     since these tests need read-your-writes across write then read.
/// </summary>
public class MenuBannerEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private const string AdminKey = "correct-key";
	private readonly string _dataDir = Directory.CreateTempSubdirectory("basil-menu-banner-tests-").FullName;
	private readonly WebApplicationFactory<Program> _factory;

	public MenuBannerEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton<IMenuBannerRepository>(new InMemoryMenuBannerRepository());
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

	public void Dispose()
	{
		if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string host = "api.test.local",
		string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = host } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private static HttpRequestMessage MakeCreateRequest(string image, string url, DateTime? begins, DateTime? expires)
	{
		var request = MakeRequest(HttpMethod.Post, "/menu/banners");
		request.Content = new StringContent(
			JsonSerializer.Serialize(new { image, url, begins, expires }), Encoding.UTF8, "application/json");
		return request;
	}

	[Fact]
	public async Task PostBanner_ExternalUrl_CreatesEntry()
	{
		var begins = DateTime.UtcNow.AddDays(-1);
		var expires = DateTime.UtcNow.AddDays(1);

		var response = await _factory.CreateClient()
			.SendAsync(MakeCreateRequest("https://example.test/banner.png", "https://example.test/event", begins,
				expires));

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		var body = await response.Content.ReadFromJsonAsync<Envelope<JsonElement>>();
		Assert.Equal("https://example.test/banner.png", body!.Data!.GetProperty("image").GetString());
	}

	[Fact]
	public async Task PostBanner_ImageNotAUrl_ReturnsBadRequest()
	{
		var response = await _factory.CreateClient()
			.SendAsync(MakeCreateRequest("not-a-url", "https://example.test", DateTime.UtcNow, DateTime.UtcNow));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PostBanner_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeCreateRequest("https://example.test/b.png", "https://example.test", DateTime.UtcNow,
			DateTime.UtcNow);
		request.Headers.Authorization = null;

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PostBannerImage_Upload_StoresFileAndResolvesToAssetsUrl()
	{
		var client = _factory.CreateClient();
		var createResponse = await client.SendAsync(MakeCreateRequest("https://example.test/b.png",
			"https://example.test", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
		var created = await createResponse.Content.ReadFromJsonAsync<Envelope<JsonElement>>();
		var id = created!.Data!.GetProperty("id").GetInt32();

		var uploadRequest = MakeRequest(HttpMethod.Post, $"/menu/banners/{id}/image");
		uploadRequest.Content = new MultipartFormDataContent
			{ { new ByteArrayContent([1, 2, 3]), "file", "banner.png" } };
		var uploadResponse = await client.SendAsync(uploadRequest);
		var uploaded = await uploadResponse.Content.ReadFromJsonAsync<Envelope<JsonElement>>();

		Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
		var image = uploaded!.Data!.GetProperty("image").GetString();
		Assert.StartsWith("https://assets.test.local/menu/banners/", image);
		Assert.Single(Directory.EnumerateFiles(Path.Combine(_dataDir, "Banners")));
	}

	[Fact]
	public async Task GetBanner_Unknown_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menu/banners/999"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PatchBanner_UpdatesFields()
	{
		var client = _factory.CreateClient();
		var createResponse = await client.SendAsync(MakeCreateRequest("https://example.test/b.png",
			"https://example.test", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
		var created = await createResponse.Content.ReadFromJsonAsync<Envelope<JsonElement>>();
		var id = created!.Data!.GetProperty("id").GetInt32();

		var patchRequest = MakeRequest(HttpMethod.Patch, $"/menu/banners/{id}");
		patchRequest.Content =
			new StringContent("""{"url":"https://example.test/updated"}""", Encoding.UTF8, "application/json");
		var patchResponse = await client.SendAsync(patchRequest);
		var patched = await patchResponse.Content.ReadFromJsonAsync<Envelope<JsonElement>>();

		Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
		Assert.Equal("https://example.test/updated", patched!.Data!.GetProperty("url").GetString());
	}

	[Fact]
	public async Task DeleteBanner_Existing_RemovesIt()
	{
		var client = _factory.CreateClient();
		var createResponse = await client.SendAsync(MakeCreateRequest("https://example.test/b.png",
			"https://example.test", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
		var created = await createResponse.Content.ReadFromJsonAsync<Envelope<JsonElement>>();
		var id = created!.Data!.GetProperty("id").GetInt32();

		var deleteResponse = await client.SendAsync(MakeRequest(HttpMethod.Delete, $"/menu/banners/{id}"));
		Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, $"/menu/banners/{id}"));
		Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
	}

	[Fact]
	public async Task GetMenuContent_ReturnsBannersWithCorrectIsCurrent()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeCreateRequest("https://example.test/current.png", "https://example.test",
			DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
		await client.SendAsync(MakeCreateRequest("https://example.test/future.png", "https://example.test",
			DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(20)));

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu-content.json",
			"assets.test.local", null));
		var body = await response.Content.ReadFromJsonAsync<JsonElement>();

		response.EnsureSuccessStatusCode();
		var images = body.GetProperty("images").EnumerateArray().ToList();
		Assert.Equal(2, images.Count);
		Assert.True(images.Single(i => i.GetProperty("image").GetString()!.Contains("current"))
			.GetProperty("IsCurrent").GetBoolean());
		Assert.False(images.Single(i => i.GetProperty("image").GetString()!.Contains("future"))
			.GetProperty("IsCurrent").GetBoolean());
	}

	[Fact]
	public async Task PostBanner_NullBeginsAndExpires_IsAlwaysCurrentWithNullBoundsInManifest()
	{
		var client = _factory.CreateClient();
		var response = await client.SendAsync(
			MakeCreateRequest("https://example.test/permanent.png", "https://example.test", null, null));
		var created = await response.Content.ReadFromJsonAsync<Envelope<JsonElement>>();

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.True(created!.Data!.GetProperty("isCurrent").GetBoolean());
		Assert.Equal(JsonValueKind.Null, created.Data!.GetProperty("begins").ValueKind);
		Assert.Equal(JsonValueKind.Null, created.Data!.GetProperty("expires").ValueKind);

		var manifestResponse =
			await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu-content.json", "assets.test.local", null));
		var manifest = await manifestResponse.Content.ReadFromJsonAsync<JsonElement>();
		var image = manifest.GetProperty("images").EnumerateArray()
			.Single(i => i.GetProperty("image").GetString()!.Contains("permanent"));

		Assert.True(image.GetProperty("IsCurrent").GetBoolean());
		Assert.Equal(JsonValueKind.Null, image.GetProperty("begins").ValueKind);
		Assert.Equal(JsonValueKind.Null, image.GetProperty("expires").ValueKind);
	}

	[Fact]
	public async Task PostBanner_OnlyExpiresSet_BeginsHasNoLowerBound()
	{
		var client = _factory.CreateClient();
		var response = await client.SendAsync(MakeCreateRequest("https://example.test/expiring.png",
			"https://example.test", null, DateTime.UtcNow.AddDays(1)));
		var created = await response.Content.ReadFromJsonAsync<Envelope<JsonElement>>();

		Assert.True(created!.Data!.GetProperty("isCurrent").GetBoolean());
		Assert.Equal(JsonValueKind.Null, created.Data!.GetProperty("begins").ValueKind);
	}
}