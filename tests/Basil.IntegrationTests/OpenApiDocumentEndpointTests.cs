using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Configurations;
using Basil.Web;
using Basil.Web.OpenApi;
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
					["Basil:Bot:CommandPrefix"] = "!"
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
	[InlineData("bancho", "osu! Client API: Bancho Protocol", new[] { "/" })]
	[InlineData("osuweb", "osu! Client API: osu! Web", new[] { "/web/osu-search.php", "/difficulty-rating" })]
	[InlineData("beatmapassets", "osu! Client API: Beatmap Assets",
		new[] { "/thumb/{setId}l.jpg", "/thumb/{setId}.jpg" })]
	[InlineData("avatar", "osu! Client API: Avatar Files", new[] { "/{userId}" })]
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
		Assert.Equal(expectedTitle, document.Info.Title);
		foreach (var path in expectedPaths) Assert.Contains(path, document.Paths.Keys);
	}

	[Fact]
	public async Task BasilApiDocument_NoRoutePathHasABareIdSegment()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
		var document = await response.Content.ReadFromJsonAsync<OpenApiDocumentShape>();

		Assert.NotNull(document);
		Assert.DoesNotContain(document.Paths.Keys, path => path.Contains("{id}") || path.Contains("{id:"));
	}

	[Fact]
	public async Task BanchoDocument_DoesNotContainOsuWebRoutes()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/bancho.json"));
		var document = await response.Content.ReadFromJsonAsync<OpenApiDocumentShape>();

		Assert.NotNull(document);
		Assert.DoesNotContain("/web/osu-search.php", document.Paths.Keys);
	}

	[Theory]
	[InlineData("/docs/")]
	[InlineData("/docs/osu-client/")]
	[InlineData("/docs/basil-api/")]
	[InlineData("/docs/basil-bot/")]
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
		var body = await response.Content.ReadFromJsonAsync<Envelope<HealthShape>>();

		response.EnsureSuccessStatusCode();
		Assert.Equal("ok", body!.Data!.Status);
	}

	/// <summary>
	///     Every declared basilapi JSON response schema must be the Enveloped Response Standard shape,
	///     not the bare handler type — the exact defect flagged as "schema vs example mismatch" (an
	///     auto-generated client would parse the wrong shape otherwise). SSE-only routes are the one
	///     exception (their payload is never enveloped) and are checked separately below.
	/// </summary>
	[Fact]
	public async Task BasilApiDocument_JsonResponseSchemasAreEnveloped()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
		var document = await response.Content.ReadFromJsonAsync<JsonElement>();

		var schema = document
			.GetProperty("paths").GetProperty("/scores").GetProperty("get")
			.GetProperty("responses").GetProperty("200").GetProperty("content")
			.GetProperty("application/json").GetProperty("schema");

		// The envelope's six non-`data` fields live on the shared `Envelope` component, combined via
		// `allOf` with a per-operation `{ data }` object — not re-inlined as flat "properties" on every
		// single response (see EnvelopeSchemaTransformer).
		var allOf = schema.GetProperty("allOf").EnumerateArray().ToList();
		Assert.Equal("#/components/schemas/Envelope", allOf[0].GetProperty("$ref").GetString());

		var envelopeProps = document.GetProperty("components").GetProperty("schemas").GetProperty("Envelope")
			.GetProperty("properties").EnumerateObject().Select(p => p.Name);
		var dataProps = allOf[1].GetProperty("properties").EnumerateObject().Select(p => p.Name);

		Assert.Equal(["success", "code", "message", "data", "meta", "errors", "timestamp"],
			envelopeProps.Concat(dataProps).ToHashSet());
	}

	[Fact]
	public async Task BasilApiDocument_SseRouteResponseSchemaIsNotEnveloped()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
		var document = await response.Content.ReadFromJsonAsync<JsonElement>();

		var responseNode = document
			.GetProperty("paths").GetProperty("/matches/{matchId}/live").GetProperty("get")
			.GetProperty("responses").GetProperty("200").GetProperty("content")
			.GetProperty("application/json");

		// Bare $ref to the declared payload type (MatchLiveSnapshot), not an inline Envelope object —
		// SSE payloads are never enveloped at runtime, so neither is their declared schema.
		Assert.Equal("#/components/schemas/MatchLiveSnapshot", responseNode.GetProperty("schema")
			.GetProperty("$ref").GetString());

		// The example must also stay unwrapped (no top-level "success"/"data" envelope keys) —
		// this route's path carries the literal `live` segment, so OpenApiExampleExtensions.WithExample
		// must skip the same envelope-wrapping it applies to every other basilapi route.
		var examplePropertyNames = responseNode.GetProperty("example").EnumerateObject()
			.Select(p => p.Name).ToHashSet();
		Assert.DoesNotContain("success", examplePropertyNames);
		Assert.Contains("inProgress", examplePropertyNames);
	}

	[Fact]
	public async Task BasilApiDocument_DeclaresAdminKeySecurityScheme_AndAppliesItToAdminRoutes()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
		var document = await response.Content.ReadFromJsonAsync<JsonElement>();

		var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("AdminKey");
		Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
		Assert.Equal("X-Admin-Key", scheme.GetProperty("name").GetString());
		Assert.Equal("header", scheme.GetProperty("in").GetString());

		var adminOp = document.GetProperty("paths").GetProperty("/users/{userId}").GetProperty("put");
		Assert.True(adminOp.GetProperty("security")[0].TryGetProperty("AdminKey", out _));

		var publicOp = document.GetProperty("paths").GetProperty("/scores").GetProperty("get");
		Assert.False(publicOp.TryGetProperty("security", out _));
	}

	[Fact]
	public async Task BasilApiDocument_PatchRequestSchemaHasNoRequiredFields_UnlikePut()
	{
		var client = _factory.CreateClient();

		var response = await client.SendAsync(MakeRequest("/openapi/basilapi.json"));
		var document = await response.Content.ReadFromJsonAsync<JsonElement>();
		var schemas = document.GetProperty("components").GetProperty("schemas");

		Assert.False(schemas.GetProperty("UpdateUserRequest").TryGetProperty("required", out _));
		var replaceRequired = schemas.GetProperty("ReplaceUserRequest").GetProperty("required")
			.EnumerateArray().Select(e => e.GetString()!).ToHashSet();
		Assert.Equal(["name", "country", "privilege"], replaceRequired);
	}

	private sealed record HealthShape(string Status);

	private sealed record OpenApiDocumentShape(OpenApiInfoShape Info, Dictionary<string, object> Paths);

	private sealed record OpenApiInfoShape(string Title);
}