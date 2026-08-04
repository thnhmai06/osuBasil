using System.Net;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Web;
using Basil.Web.Routing.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers <see cref="UserLookup" />: every public `GET /users/{idOrName}...`
///     route accepts a username in place of the numeric id, resolving via
///     <see cref="IUserRepository.FetchByNameAsync" /> and 302-redirecting to the canonical numeric
///     path. A numeric segment is served directly (not redirected); an unknown username 404s.
/// </summary>
public class UserLookupEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly Dictionary<string, User> _byName = [];
	private readonly WebApplicationFactory<Program> _factory;

	public UserLookupEndpointTests(WebApplicationFactory<Program> factory)
	{
		var users = Substitute.For<IUserRepository>();
		users.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => _byName.Values.FirstOrDefault(u => u.Id == call.ArgAt<int>(0)));
		users.FetchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => _byName.GetValueOrDefault(call.ArgAt<string>(0)));
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
	public async Task GetUser_ByUsername_RedirectsToCanonicalId()
	{
		_byName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

		var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player", "correct-key"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/users/7", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task GetUser_ByUnknownUsername_ReturnsNotFound()
	{
		var response = await MakeClient().SendAsync(MakeRequest("/users/nobody", "correct-key"));

		Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
	}

	[Fact]
	public async Task GetUserAvatar_ByUsername_RedirectsToCanonicalId()
	{
		_byName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

		var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player/avatar", "correct-key"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/users/7/avatar", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task GetUserLive_ByUsername_RedirectsToCanonicalId()
	{
		_byName["cool_player"] = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);

		var response = await MakeClient().SendAsync(MakeRequest("/users/cool_player/live"));

		Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
		Assert.Equal("/users/7/live", response.Headers.Location?.ToString());
	}

	[Fact]
	public async Task GetUser_NumericId_IsNotRedirected()
	{
		var response = await MakeClient().SendAsync(MakeRequest("/users/999", "correct-key"));

		Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
	}
}