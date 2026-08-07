using System.Net;
using System.Net.Http.Json;
using Basil.Application.Configurations;
using Basil.Domain.Users;
using Basil.Web;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Verifies the admin-key gate on the management REST API: missing or wrong admin keys are
///     rejected with 401 (not 200) across endpoints, the correct key succeeds, and the BasilBot user
///     id is protected from edits.
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
					["Basil:Bot:CommandPrefix"] = "!"
				});
			});
			builder.ConfigureServices(services =>
			{
				services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" }));
				services.AddSingleton(TestDoubles.FixedAdminKeySettingsRepository());
				services.AddSingleton(TestDoubles.NullUserRepository());
			});
		});
	}

	private static HttpRequestMessage MakeRequest(HttpMethod method, string path, string? adminKey = null)
	{
		var request = new HttpRequestMessage(method, path) { Headers = { Host = "api.test.local" } };
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	[Theory]
	[InlineData(null)]
	[InlineData("wrong-key")]
	public async Task DeleteUser_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/users/1", adminKey));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("wrong-key")]
	public async Task DeleteMapset_MissingOrWrongAdminKey_ReturnsUnauthorized(string? adminKey)
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/beatmapsets/1", adminKey));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	// Block/unblock (POST/DELETE /users/{id}/block/{targetId}) was dropped entirely per the /user
	// redesign — no replacement route exists, so this can no longer succeed.
	[Fact]
	public async Task BlockUser_RouteNoLongerExists()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Post, "/users/1/block/2", "correct-key"));

		Assert.False(response.IsSuccessStatusCode);
	}

	[Fact]
	public async Task GetUsers_MissingAdminKey_ReturnsUnauthorized()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users"));

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task GetUsers_CorrectAdminKey_ReturnsOk()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users", "correct-key"));

		response.EnsureSuccessStatusCode();
	}

	[Fact]
	public async Task CreateUser_InvalidUsername_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Post, "/users", "correct-key");
		// Constructed as a plain anonymous object (country as the wire's lowercase acronym string, not
		// the C# Country enum) since JsonContent.Create with no explicit JsonSerializerOptions doesn't
		// know about CountryJsonConverter — matches every other body literal in this test suite.
		request.Content = JsonContent.Create(new
			{ name = "ab", password = "hunter2", country = "xx", privilege = (int)UserPrivileges.Unrestricted });

		var response = await client.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Contains("between 3 and 15 characters", body);
	}

	[Fact]
	public async Task GetUserAvatar_NoAdminKey_IsPublicAndReturnsNotFoundWhenNoneUploaded()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users/1/avatar"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetUserAvatar_NoAvatarUploaded_ReturnsNotFound()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users/1/avatar", "correct-key"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task DeleteUser_BasilBotId_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Delete, "/users/0", "correct-key"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task UpdateUser_BasilBotId_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var request = MakeRequest(HttpMethod.Patch, "/users/0", "correct-key");
		request.Content = JsonContent.Create(new UpdateUserRequest("newname"));

		var response = await client.SendAsync(request);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task GetUserLive_BasilBotId_ReturnsBadRequest()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest(HttpMethod.Get, "/users/0/live"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}
}