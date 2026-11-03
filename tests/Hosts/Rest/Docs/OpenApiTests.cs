using System.Text.Json.Nodes;

namespace Basil.Host.Rest.Tests.Docs;

/// <summary>The generated OpenAPI document is the one reference of the API (ADR-009).</summary>
[Collection("REST host")]
public sealed class OpenApiTests(RestHostFixture fixture)
{
	private async Task<JsonNode> DocumentAsync()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		using var response = await client.GetAsync("/openapi/v1.json");
		Assert.Equal(200, (int)response.StatusCode);
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	[Fact]
	public async Task Document_ListsEveryRoomCommandAsItsOwnRoute()
	{
		var paths = (await DocumentAsync())["paths"]!.AsObject();

		foreach (var command in new[]
		         {
			         "close", "lock", "unlock", "size", "move", "host", "clearhost", "set", "start", "abort", "timer",
			         "aborttimer", "team", "map", "mods", "kick", "ban", "unban", "invite", "password", "name",
			         "private", "addref", "removeref"
		         })
			Assert.NotNull(paths[$"/v1/rooms/{{id}}/{command}"]?["post"]);
	}

	[Fact]
	public async Task Document_MarksTokenRoutesWithTheBearerScheme_AndPublicRoutesWithout()
	{
		var paths = (await DocumentAsync())["paths"]!;

		Assert.NotNull(paths["/v1/rooms"]!["get"]!["security"]);
		Assert.Null(paths["/v1/users/{id}"]!["get"]!["security"]);
		Assert.Null(paths["/v1/matches/{id}"]!["get"]!["security"]);
		Assert.NotNull(paths["/v1/matches/{id}"]!["get"]!["responses"]!["200"]!["content"]!["text/event-stream"]);
	}

	[Fact]
	public async Task Document_EverySummaryIsOneSentence_AndNothingNamesTheImplementation()
	{
		var paths = (await DocumentAsync())["paths"]!.AsObject();

		foreach (var (path, item) in paths)
		foreach (var (method, operation) in item!.AsObject())
		{
			var summary = (string?)operation!["summary"];
			Assert.True(!string.IsNullOrWhiteSpace(summary), $"{method} {path} has no summary");
			Assert.EndsWith(".", summary);
			var text = summary + (string?)operation["description"];
			foreach (var forbidden in new[] { "PostgreSQL", "Redis", "Channel<", "middleware", "ImageSharp" })
				Assert.DoesNotContain(forbidden, text);
		}
	}

	private async Task<JsonNode> StableDocumentAsync()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		using var response = await client.GetAsync("/openapi/osu-stable.json");
		Assert.Equal(200, (int)response.StatusCode);
		return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
	}

	[Theory]
	[InlineData("/web/osu-osz2-getscores.php", "get")]
	[InlineData("/web/osu-getfriends.php", "get")]
	[InlineData("/web/osu-getbeatmapinfo.php", "post")]
	[InlineData("/web/osu-submit-modular-selector.php", "post")]
	[InlineData("/web/osu-getreplay.php", "get")]
	[InlineData("/web/osu-screenshot.php", "post")]
	[InlineData("/web/lastfm.php", "get")]
	[InlineData("/web/osu-search.php", "get")]
	[InlineData("/web/osu-search-set.php", "get")]
	[InlineData("/d/{setId}", "get")]
	[InlineData("/users", "post")]
	[InlineData("/", "post")]
	public async Task StableDocument_ListsTheEndpointsTheClientCalls(string path, string method)
	{
		var paths = (await StableDocumentAsync())["paths"]!.AsObject();

		Assert.NotNull(paths[path]?[method]);
	}

	[Fact]
	public async Task StableDocument_ListsThePacketsByTheirIds_AndEverySummaryIsOneSentence()
	{
		var document = await StableDocumentAsync();
		var paths = document["paths"]!.AsObject();

		Assert.Contains("| 0 | `ChangeAction` |", (string?)paths["/"]!["post"]!["description"]);
		foreach (var (path, item) in paths)
		foreach (var (method, operation) in item!.AsObject())
		{
			var summary = (string?)operation!["summary"];
			Assert.True(!string.IsNullOrWhiteSpace(summary), $"{method} {path} has no summary");
			Assert.EndsWith(".", summary);
		}
	}

	[Fact]
	public async Task TheTwoDocuments_DoNotListEachOthersEndpoints()
	{
		var rest = (await DocumentAsync())["paths"]!.AsObject();
		var osu = (await StableDocumentAsync())["paths"]!.AsObject();

		Assert.All(rest, path => Assert.StartsWith("/v1/", path.Key));
		Assert.DoesNotContain(osu, path => path.Key.StartsWith("/v1/", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Docs_AndHealth_ArePublic()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		using var health = await client.GetAsync("/health");
		using var docs = await client.GetAsync("/docs/v1");
		using var osuDocs = await client.GetAsync("/docs/osu-stable");

		Assert.Equal(200, (int)health.StatusCode);
		Assert.Equal(200, (int)docs.StatusCode);
		Assert.Equal(200, (int)osuDocs.StatusCode);
	}
}