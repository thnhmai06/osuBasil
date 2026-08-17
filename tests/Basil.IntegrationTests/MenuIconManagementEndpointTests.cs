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
///     Covers `/menu/icon` (`GET`/`PUT`/`PATCH`/`DELETE`, metadata only) and `/menu/icon/image`
///     (`POST`/`DELETE`, the uploaded-file form). Backed by a real, stateful
///     <see cref="InMemorySettingsRepository" /> (not a canned substitute), since these tests need
///     read-your-writes across write then read. An uploaded icon's bytes still land on disk under
///     `Data/Menu/Icon.{ext}` (not a `StorageOptions` path, so — unlike avatar/seasonal tests — these
///     tests share that fixed location and must clean it up themselves), even though the pointer to
///     it lives in the (in-memory, per-test) Settings repository.
/// </summary>
public class MenuIconManagementEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
	private const string AdminKey = "correct-key";
	private static readonly string MenuDir = Path.Combine(AppContext.BaseDirectory, "Data", "Menu");
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
		if (!Directory.Exists(MenuDir)) return;
		foreach (var file in Directory.EnumerateFiles(MenuDir, "Icon.*")) File.Delete(file);
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = AdminKey)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	private static HttpRequestMessage MakeUploadRequest(string fileName = "icon.png")
	{
		var request = MakeRequest(HttpMethod.Post, "/menu/icon/image");
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", fileName } };
		return request;
	}

	[Fact]
	public async Task GetIcon_NotSet_ReturnsNullImageAndUrl()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Contains("\"image\":null", body);
		Assert.Contains("\"url\":null", body);
	}

	[Fact]
	public async Task PostIconImage_ValidUpload_StoresFileAndIsReflectedInGet()
	{
		var client = _factory.CreateClient();

		var postResponse = await client.SendAsync(MakeUploadRequest());
		Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
		Assert.True(File.Exists(Path.Combine(MenuDir, "Icon.png")));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
		Assert.Contains("https://assets.test.local/menu/icon", body);
	}

	[Fact]
	public async Task PostIconImage_SecondUploadWithDifferentExtension_ReplacesFirstUpload_Upsert()
	{
		var client = _factory.CreateClient();

		await client.SendAsync(MakeUploadRequest("first.png"));
		var response = await client.SendAsync(MakeUploadRequest("second.jpg"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.False(File.Exists(Path.Combine(MenuDir, "Icon.png")));
		Assert.True(File.Exists(Path.Combine(MenuDir, "Icon.jpg")));
	}

	[Fact]
	public async Task PostIconImage_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Post, "/menu/icon/image", null);
		request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "icon.png" } };

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_ExternalUrl_ReplacesUploadAndIsReflectedInGet()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var patchRequest = MakeRequest(HttpMethod.Patch, "/menu/icon");
		patchRequest.Content = new StringContent("""{"image":"https://example.test/icon.png"}""", Encoding.UTF8,
			"application/json");
		var patchResponse = await client.SendAsync(patchRequest);

		Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
		Assert.Empty(Directory.EnumerateFiles(MenuDir, "Icon.*"));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Contains("https://example.test/icon.png", body);
	}

	[Fact]
	public async Task PatchIcon_ImageNotAUrl_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Patch, "/menu/icon");
		request.Content = new StringContent("""{"image":"not-a-url"}""", Encoding.UTF8, "application/json");
		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_MissingAdminKey_ReturnsUnauthorized()
	{
		var request = MakeRequest(HttpMethod.Patch, "/menu/icon", null);
		request.Content = new StringContent("""{"image":"https://example.test/icon.png"}""", Encoding.UTF8,
			"application/json");

		var response = await _factory.CreateClient().SendAsync(request);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task PatchIcon_UrlOnly_UpdatesClickThroughWithoutTouchingImage()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var patchRequest = MakeRequest(HttpMethod.Patch, "/menu/icon");
		patchRequest.Content = new StringContent("""{"url":"https://example.test/click"}""", Encoding.UTF8,
			"application/json");
		await client.SendAsync(patchRequest);

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Contains("https://example.test/click", body);
		Assert.Contains("https://assets.test.local/menu/icon", body);
	}

	[Fact]
	public async Task DeleteIcon_ExistingUpload_RemovesFileClearsUrlAndReturnsOk()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/menu/icon"));
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Empty(Directory.EnumerateFiles(MenuDir, "Icon.*"));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Contains("\"image\":null", body);
		Assert.Contains("\"url\":null", body);
	}

	[Fact]
	public async Task DeleteIcon_NoIconSet_StillReturnsOk_Idempotent()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/menu/icon"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task DeleteIcon_MissingAdminKey_ReturnsUnauthorized()
	{
		var response = await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/menu/icon", null));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task DeleteIconImage_ExistingUpload_RemovesFileButKeepsUrl()
	{
		var client = _factory.CreateClient();
		await client.SendAsync(MakeUploadRequest());
		var patchRequest = MakeRequest(HttpMethod.Patch, "/menu/icon");
		patchRequest.Content = new StringContent("""{"url":"https://example.test/click"}""", Encoding.UTF8,
			"application/json");
		await client.SendAsync(patchRequest);

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/menu/icon/image"));
		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Empty(Directory.EnumerateFiles(MenuDir, "Icon.*"));

		var getResponse = await client.SendAsync(MakeRequest(HttpMethod.Get, "/menu/icon"));
		var body = await getResponse.Content.ReadAsStringAsync();
		Assert.Contains("\"image\":null", body);
		Assert.Contains("https://example.test/click", body);
	}

	[Fact]
	public async Task DeleteIconImage_MissingAdminKey_ReturnsUnauthorized()
	{
		var response =
			await _factory.CreateClient().SendAsync(MakeRequest(HttpMethod.Delete, "/menu/icon/image", null));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}
}
