using System.Net;
using System.Net.Http.Json;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Web;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers <see cref="Basil.Web.Middleware.ExceptionLoggingMiddleware" />: an unhandled
///     exception thrown by a route handler on the <c>api.</c> host must still produce the
///     Enveloped Response Standard shape, not a bare, unenveloped 500.
/// </summary>
public class ExceptionEnvelopeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
	private readonly WebApplicationFactory<Program> _factory;

	public ExceptionEnvelopeEndpointTests(WebApplicationFactory<Program> factory)
	{
		var users = Substitute.For<IUserRepository>();
		users.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Throws(new InvalidOperationException("boom"));

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
				services.AddSingleton(TestDoubles.BypassAdminKeySettingsRepository());
				services.AddSingleton(users);
			});
		});
	}

	private static HttpRequestMessage MakeRequest(string path)
	{
		return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
	}

	[Fact]
	public async Task RouteHandlerThrows_ReturnsEnvelopedServerError()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/users/7"));

		Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
		var envelope = await response.Content.ReadFromJsonAsync<Envelope<object?>>();
		Assert.NotNull(envelope);
		Assert.False(envelope.Success);
		Assert.Equal(500, envelope.Code);
	}
}