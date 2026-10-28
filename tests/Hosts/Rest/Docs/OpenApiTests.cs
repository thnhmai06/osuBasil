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

		foreach (var command in new[] { "close", "lock", "unlock", "size", "move", "host", "clearhost", "set", "start", "abort", "timer", "aborttimer", "team", "map", "mods", "kick", "ban", "unban", "invite", "password", "name", "private", "addref", "removeref" })
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

	[Fact]
	public async Task Docs_AndHealth_ArePublic()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		using var health = await client.GetAsync("/health");
		using var docs = await client.GetAsync("/docs/v1");

		Assert.Equal(200, (int)health.StatusCode);
		Assert.Equal(200, (int)docs.StatusCode);
	}
}
