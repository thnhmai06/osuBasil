using System.Net;
using Basil.Application.Configuration;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Covers the `/b`, `/m`, `/u`, `/s`, `/ss` shorthand redirects: bare prefix and prefix-plus-rest
///     both 302 to the canonical plural path, preserving whatever query string was attached.
/// </summary>
public class AbbreviationRedirectEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AbbreviationRedirectEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Basil:Server:Domain"] = "test.local",
                    ["Basil:Bot:CommandPrefix"] = "!",
                    ["Basil:Server:MenuIconPath"] = "icon.png",
                    ["Basil:Server:MenuOnclickUrl"] = "https://example.test"
                });
            });
            builder.ConfigureServices(services =>
                services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Path = "" })));
        });
    }

    private HttpClient MakeClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static HttpRequestMessage MakeRequest(string path)
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
    }

    [Theory]
    [InlineData("/b", "/beatmapsets")]
    [InlineData("/m", "/matches")]
    [InlineData("/u", "/users")]
    [InlineData("/s", "/scores")]
    [InlineData("/ss", "/seasonals")]
    public async Task BarePrefix_RedirectsToCanonicalRoot(string prefix, string target)
    {
        var response = await MakeClient().SendAsync(MakeRequest(prefix));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/b/100", "/beatmapsets/100")]
    [InlineData("/m/5", "/matches/5")]
    [InlineData("/u/7", "/users/7")]
    [InlineData("/s/42", "/scores/42")]
    [InlineData("/ss/winter.png", "/seasonals/winter.png")]
    public async Task PrefixWithRest_RedirectsToCanonicalPath(string path, string target)
    {
        var response = await MakeClient().SendAsync(MakeRequest(path));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(target, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PrefixWithRest_PreservesQueryString()
    {
        var response = await MakeClient().SendAsync(MakeRequest("/m/5/live?foo=bar"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/matches/5/live?foo=bar", response.Headers.Location?.ToString());
    }
}
