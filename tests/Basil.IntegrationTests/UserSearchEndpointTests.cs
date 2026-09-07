using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Formats;
using Basil.Application.Services.Users;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Server;
using Basil.Server.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers `GET /users/search`'s route wiring: `q` is required, `page`/`pageSize` normalize and
///     drive the repository's paging, and the response is the standard `PagedResult` shape. The
///     underlying filter parsing and SQL matching are covered by
///     <c>SqliteUserRepositoryTests</c> and <c>UserSearchQueryParserTests</c> instead of being
///     re-verified here.
/// </summary>
public class UserSearchEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();
	private readonly WebApplicationFactory<Program> _factory;

	public UserSearchEndpointTests(WebApplicationFactory<Program> factory)
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
				services.AddSingleton(_users);
			});
		});
	}

	private HttpClient MakeClient()
	{
		return _factory.CreateClient();
	}

	private static HttpRequestMessage MakeRequest(string path)
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
	}

	[Fact]
	public async Task Search_MissingQ_ReturnsBadRequest()
	{
		var response = await MakeClient().SendAsync(MakeRequest("/users/search"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Search_BlankQ_ReturnsBadRequest()
	{
		var response = await MakeClient().SendAsync(MakeRequest("/users/search?q=%20"));

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Fact]
	public async Task Search_ValidQ_ReturnsPagedResult()
	{
		var user = new User(7, "cool_player", Country.Us, UserPrivileges.Unrestricted, default);
		_users.SearchAsync(Arg.Any<UserSearchFilters>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<User>>([user]));
		_users.SearchCountAsync(Arg.Any<UserSearchFilters>(), Arg.Any<CancellationToken>()).Returns(1);

		var response = await MakeClient().SendAsync(MakeRequest("/users/search?q=cool"));
		var body = await response.Content.ReadFromJsonAsync<Envelope<List<UserView>>>(BasilJsonOptions.Instance);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(1, body!.Meta!.TotalRecords);
		Assert.Equal(7, body.Data![0].Id);
	}

	[Fact]
	public async Task Search_PageAndPageSize_PassThroughToRepositoryAsOffsetAndAmount()
	{
		_users.SearchAsync(Arg.Any<UserSearchFilters>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<User>>([]));
		_users.SearchCountAsync(Arg.Any<UserSearchFilters>(), Arg.Any<CancellationToken>()).Returns(0);

		await MakeClient().SendAsync(MakeRequest("/users/search?q=cool&page=3&pageSize=10"));

		await _users.Received(1).SearchAsync(Arg.Any<UserSearchFilters>(), 20, 10, Arg.Any<CancellationToken>());
	}
}