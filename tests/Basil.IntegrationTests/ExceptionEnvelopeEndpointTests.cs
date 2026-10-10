using System.Net;
using System.Net.Http.Json;
using Basil.Application.Shared.Configuration;
using Basil.Host;
using Basil.Host.Api.Shared.Http.Middleware;
using Basil.Application.Shared.Http;
using Basil.Application.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers <see cref="ExceptionLoggingMiddleware" />: an unhandled
///     exception thrown by a route handler on the <c>api.</c> host must still produce the
///     Enveloped Response Standard shape, not a bare, unenveloped 500.
/// </summary>
public class ExceptionEnvelopeEndpointTests : IClassFixture<WebApplicationFactory<Bootstrap>>
{
	private readonly WebApplicationFactory<Bootstrap> _factory;

	public ExceptionEnvelopeEndpointTests(WebApplicationFactory<Bootstrap> factory)
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
				services.AddSingleton(Options.Create(new DatabaseOptions { Path = "" }));
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

		var response = await client.SendAsync(MakeRequest("/users/7"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
		Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
		var envelope = await response.Content.ReadFromJsonAsync<Envelope<object?>>(cancellationToken: TestContext.Current.CancellationToken);
		Assert.NotNull(envelope);
		Assert.False(envelope.Success);
		Assert.Equal(500, envelope.Code);
	}
}