using System.Net;
using System.Text;
using Basil.Application.Configurations;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `/menuicon/icon` (`GET`/`PUT`/`DELETE`) and `/menuicon/url` (`GET`/`PUT` — no `DELETE`,
///     the icon file's presence is the only on/off switch). Both are singletons backed by
///     `Data/MenuIcon.{ext}`/`Data/MenuIconUrl.txt` under the running executable's directory (not a
///     `StorageOptions` path, so — unlike avatar/seasonal tests — these tests share that fixed
///     location and must clean it up themselves).
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
					["Basil:Bot:CommandPrefix"] = "!",
					["Basil:Server:AdminKey"] = AdminKey
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
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
		var urlPath = Path.Combine(DataDir, "MenuIconUrl.txt");
		if (File.Exists(urlPath)) File.Delete(urlPath);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("X-Admin-Key", adminKey);
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