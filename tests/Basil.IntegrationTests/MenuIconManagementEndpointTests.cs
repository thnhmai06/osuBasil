using System.Net;
using System.Text;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Basil.Infrastructure.Security;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `/menuicon/icon` (`GET`/`PUT`/`PATCH`/`DELETE`) and `/menuicon/url` (`GET`/`PUT` — no
///     `DELETE`, the icon's presence is the only on/off switch). Backed by a real, stateful
///     <see cref="InMemorySettingsRepository" /> (not a canned substitute), since these tests need
///     read-your-writes across `PUT`/`PATCH`/`DELETE` then `GET`. An uploaded icon's bytes still land
///     on disk under `Data/MenuIcon.{ext}` (not a `StorageOptions` path, so — unlike avatar/seasonal
///     tests — these tests share that fixed location and must clean it up themselves), even though
///     the pointer to it now lives in the (in-memory, per-test) Settings repository.
/// </summary>
public class MenuIconManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private const string AdminKey = "correct-key";
	private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "Data");
	private readonly WebApplicationFactory<Program> _factory;

	public MenuIconManagementEndpointTests(WebApplicationFactory<Program> factory)
	{
		CleanUpFiles();
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
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				var settings = new InMemorySettingsRepository()
					.Seed("AdminKey:Hash", new BCryptPasswordHasher().Hash(Encoding.UTF8.GetBytes(AdminKey)))
					.Seed("AdminKey:LastChanged", DateTimeOffset.UtcNow.ToString("O"));
				services.AddSingleton<ISettingsRepository>(settings);
			});
		});
	}

	public void Dispose()
	{
		CleanUpFiles();
	}

	private static void CleanUpFiles()
	{
		if (!Directory.Exists(DataDir)) return;
		foreach (var file in Directory.EnumerateFiles(DataDir, "MenuIcon.*")) File.Delete(file);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private static HttpRequestMessage MakeUploadRequest(string fileName = "icon.png")
	{
		var request = MakeRequest(HttpMethod.Put, "/menuicon/icon");
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", fileName } };
		return request;
	}

	[Fact]
	public async Task GetIcon_NotSet_ReturnsNotFound()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/icon"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task PutIcon_ValidUpload_StoresFileAndIsServedByGet()
	{
		var client = _factory.CreateClient();

		var putResponse = await client.SendAsync(MakeUploadRequest());
		Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
		Assert.True(File.Exists(Path.Combine(DataDir, "MenuIcon.png")));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/icon"));
		Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
		Assert.Equal("image/png", getResponse.Content.Headers.ContentType?.MediaType);
	}

	[Fact]
	public async Task GetIcon_ConditionalRequestMatchingLastModified_ReturnsNotModifiedWithEmptyBody()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var firstResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/icon"));
		var lastModified = firstResponse.Content.Headers.LastModified;
		Assert.NotNull(lastModified);

		var conditionalRequest = MakeRequest(HttpMethod.Get, "/menuicon/icon");
		conditionalRequest.Headers.IfModifiedSince = lastModified;
		var conditionalResponse = await client.SendAsync(conditionalRequest);

		Assert.Equal(HttpStatusCode.NotModified, conditionalResponse.StatusCode);
		Assert.Empty(await conditionalResponse.Content.ReadAsByteArrayAsync());
	}

	[Fact]
	public async Task PutIcon_SecondUploadWithDifferentExtension_ReplacesFirstUpload_Upsert()
	{
		var client = _factory.CreateClient();

		await client.SendAsync(MakeUploadRequest("first.png"));
		var response = await client.SendAsync(MakeUploadRequest("second.jpg"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.False(File.Exists(Path.Combine(DataDir, "MenuIcon.png")));
		Assert.True(File.Exists(Path.Combine(DataDir, "MenuIcon.jpg")));
	}

	[Fact]
	public async Task PutIcon_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Put, "/menuicon/icon", null);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "icon.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_ExternalUrl_ReplacesUploadAndIsNotServedByGet()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var patchRequest = MakeRequest(HttpMethod.Patch, "/menuicon/icon");
		patchRequest.Content = new StringContent("""{"url":"https://example.test/icon.png"}""", Encoding.UTF8,
			"application/json");
		var patchResponse = await client.SendAsync(patchRequest);

		Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
		Assert.Empty(Directory.EnumerateFiles(DataDir, "MenuIcon.*"));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/icon"));
		Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_NotAUrl_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Patch, "/menuicon/icon");
		request.Content = new StringContent("""{"url":"not-a-url"}""", Encoding.UTF8, "application/json");
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Patch, "/menuicon/icon", null);
		request.Content = new StringContent("""{"url":"https://example.test/icon.png"}""", Encoding.UTF8,
			"application/json");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task DeleteIcon_ExistingUpload_RemovesFileAndReturnsOk()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/menuicon/icon"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Empty(Directory.EnumerateFiles(DataDir, "MenuIcon.*"));
	}

	[Fact]
	public async Task DeleteIcon_NoIconSet_StillReturnsOk_Idempotent()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/menuicon/icon"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task DeleteIcon_MissingAdminKey_ReturnsUnauthorized()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/menuicon/icon", null));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task GetUrl_NothingSet_ReturnsNull()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/url"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"url\":null", body);
	}

	[Fact]
	public async Task GetUrl_IconSetButUrlNot_ReturnsHardcodedDefault()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/url"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("https://github.com/thnhmai06/osuBasil", body);
	}

	[Fact]
	public async Task PutUrl_ThenGet_ReturnsStoredValue()
	{
		var client = _factory.CreateClient();
		var putRequest = MakeRequest(HttpMethod.Put, "/menuicon/url");
		putRequest.Content = new StringContent("""{"url":"https://example.test/icon"}""", Encoding.UTF8,
			"application/json");

		var putResponse = await client.SendAsync(putRequest);
		Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menuicon/url"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Contains("https://example.test/icon", body);
	}

	[Fact]
	public async Task PutUrl_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Put, "/menuicon/url", null);
		request.Content = new StringContent("""{"url":"https://example.test/icon"}""", Encoding.UTF8,
			"application/json");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}
