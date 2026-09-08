using System.Net;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Server.Host;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /users/{userId}...`: every route takes a numeric id only. A non-numeric segment
///     never matches the route at all (a bare, unenveloped 404), and a numeric id that doesn't exist
///     404s through the handler instead.
/// </summary>
public class UserLookupEndpointTests : IClassFixture<WebApplicationFactory<Bootstrap>>
{
	private readonly Dictionary<int, User> _byId = [];
	private readonly WebApplicationFactory<Bootstrap> _factory;

	public UserLookupEndpointTests(WebApplicationFactory<Bootstrap> factory)
	{
		var users = Substitute.For<IUserRepository>();
		users.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _byId.GetValueOrDefault(call.ArgAt<int>(0)));
		users.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<User>>([]));

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
				services.AddSingleton(users);
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
		if (adminKey is not null) request.Headers.Add("Authorization", $"Bearer {adminKey}");
		return request;
	}

	[Fact]
	public async Task GetUser_NumericId_ReturnsUser()
	{
		_byId[7] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

		var response = await MakeClient().SendAsync(MakeRequest("/users/7", "correct-key"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}

	[Fact]
	public async Task GetUser_UnknownNumericId_ReturnsNotFound()
	{
		var response = await MakeClient().SendAsync(MakeRequest("/users/999", "correct-key"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Theory]
	[InlineData("/users/cool_player")]
	[InlineData("/users/cool_player/avatar")]
	[InlineData("/users/cool_player/live")]
	public async Task GetUser_NonNumericSegment_NeverMatchesRoute(string path)
	{
		var response = await MakeClient().SendAsync(MakeRequest(path, "correct-key"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}
}