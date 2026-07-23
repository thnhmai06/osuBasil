using System.Net;
using System.Net.Http.Json;
using Basil.Application.Configuration;
using Basil.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Confirms the 5 host-group OpenAPI documents (bancho/osuweb/beatmapassets/avatar/basilapi) are
///     actually reachable and correctly partitioned — each one only carries routes from its own host
///     group (see <c>ConfigureOpenApi</c> in <c>Program.cs</c> and every <c>.WithGroupName(...)</c> in
///     <c>BanchoHostGroups.cs</c> and the other <c>Routing/</c> files). Also confirms the Scalar UI mounts
///     and the static docs pages actually respond.
/// </summary>
public class OpenApiDocumentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiDocumentEndpointTests(WebApplicationFactory<Program> factory)
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

    private static HttpRequestMessage MakeRequest(string path)
    {
        return new HttpRequestMessage(HttpMethod.Get, path) { Headers = { Host = "api.test.local" } };
    }

    [Theory]
    [InlineData("bancho", "osu! Client API — Bancho Protocol", new[] { "/" })]
    [InlineData("osuweb", "osu! Client API — osu! Web", new[] { "/web/osu-search.php", "/difficulty-rating" })]
    [InlineData("beatmapassets", "osu! Client API — Beatmap Assets", new[] { "/{path}" })]
    [InlineData("avatar", "osu! Client API — Avatar Files", new[] { "/{userId}" })]
    [InlineData("basilapi", "Basil API", new[]
    {
        "/matches/{matchId}", "/matches", "/beatmapsets/{mapsetId}", "/users", "/scores/{scoreId}",
        "/faqs/{entry}", "/seasonals/{fileName}", "/health"
    })]
    public async Task Document_ReturnsExpectedTitleAndPaths(string documentName, string expectedTitle,
        string[] expectedPaths)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest($"/openapi/{documentName}.json"));
        response.EnsureSuccessStatusCode();

        var document = await response.Content.ReadFromJsonAsync<OpenApiDocumentShape>();

        Assert.NotNull(document);
        Assert.Equal(expectedTitle, document!.Info.Title);
        foreach (var path in expectedPaths) Assert.Contains(path, document.Paths.Keys);
    }

    [Fact]
    public async Task BasilApiDocument_NoRoutePathHasABareIdSegment()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
        var document = await response.Content.ReadFromJsonAsync<OpenApiDocumentShape>();

        Assert.NotNull(document);
        Assert.DoesNotContain(document!.Paths.Keys, path => path.Contains("{id}") || path.Contains("{id:"));
    }

    [Fact]
    public async Task BanchoDocument_DoesNotContainOsuWebRoutes()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/openapi/bancho.json"));
        var document = await response.Content.ReadFromJsonAsync<OpenApiDocumentShape>();

        Assert.NotNull(document);
        Assert.DoesNotContain("/web/osu-search.php", document!.Paths.Keys);
    }

    [Theory]
    [InlineData("/docs/")]
    [InlineData("/docs/osu-client/")]
    [InlineData("/docs/basil-api/")]
    [InlineData("/docs/basilbot/")]
    public async Task DocsPage_ReturnsOk(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_RedirectsToDocs()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.SendAsync(MakeRequest("/"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/docs/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Health_ReturnsOkStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(MakeRequest("/health"));
        var body = await response.Content.ReadFromJsonAsync<HealthShape>();

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", body!.Status);
    }

    private sealed record HealthShape(string Status);

    private sealed record OpenApiDocumentShape(OpenApiInfoShape Info, Dictionary<string, object> Paths);

    private sealed record OpenApiInfoShape(string Title);
}
