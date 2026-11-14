using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>Every HTTP route has a console command, except the ones that have no meaning at a console.</summary>
[Collection("CLI host")]
public sealed partial class RouteCoverageTests(CliFixture fixture)
{
	/// <summary>The routes without a command, each with the reason.</summary>
	private static readonly Dictionary<string, string> Excluded = new()
	{
		["POST /v1/auth/login"] = "authentication: the console is not signed in",
		["POST /v1/auth/logout"] = "authentication: the console is not signed in",
		["GET /v1/auth/session"] = "the session of the caller: the console has none",
		["POST /v1/users/{id}/sessions/irc"] = "opens an IRC key for the caller's own account",
		["GET /v1/scores/{id}/replay"] = "a file download",
		["GET /v1/beatmapsets/{id}/audiopreview"] = "a file download",
		["GET /v1/beatmapsets/{id}/audio"] = "a redirect to a file download",
		["GET /v1/beatmapsets/{id}/video"] = "a redirect to a file download",
		["GET /v1/beatmapsets/{id}/background"] = "a redirect to a file download",
		["GET /v1/beatmapsets/{id}/storyboard"] = "a file download",
		["GET /v1/beatmapsets/{id}/download"] = "a file download",
		["GET /v1/beatmaps/{id}/audio"] = "a file download",
		["GET /v1/beatmaps/{id}/video"] = "a file download",
		["GET /v1/beatmaps/{id}/background"] = "a file download",
		["PUT /v1/users/{id}/friends/{otherId}"] =
			"changes the caller's own list: the console has no account of its own",
		["DELETE /v1/users/{id}/friends/{otherId}"] =
			"changes the caller's own list: the console has no account of its own",
		["PUT /v1/users/{id}/blocks/{otherId}"] =
			"changes the caller's own list: the console has no account of its own",
		["DELETE /v1/users/{id}/blocks/{otherId}"] =
			"changes the caller's own list: the console has no account of its own"
	};

	[GeneratedRegex(@"\{\*\*?(\w+)\}")]
	private static partial Regex CatchAll();

	private async Task<List<string>> RoutesAsync(bool withStreams = false)
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		using var response = await client.GetAsync("/openapi/v1.json");
		var paths = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["paths"]!.AsObject();
		var routes = new List<string>();
		foreach (var (path, item) in paths)
		foreach (var (method, operation) in item!.AsObject())
		{
			// A live stream (SSE) is watched in the dashboard, not read as one answer.
			var stream = operation!["responses"]?.AsObject()
				.Any(r => r.Value?["content"]?["text/event-stream"] is not null) == true;
			if (withStreams || !stream) routes.Add($"{method.ToUpperInvariant()} {path}");
		}

		return routes;
	}

	[Fact]
	public async Task EveryRoute_HasCommand()
	{
		var covered = CommandCatalogFactory.Create().All.SelectMany(command => command.AllRoutes)
			.Select(route => CatchAll().Replace(route, "{$1}")).ToHashSet();

		var missing = (await RoutesAsync()).Where(route => !Excluded.ContainsKey(route) && !covered.Contains(route))
			.ToList();

		Assert.True(missing.Count == 0, "Routes without a command: " + string.Join(", ", missing));
	}

	[Fact]
	public async Task EveryExclusion_RealRoute()
	{
		var routes = await RoutesAsync();

		var stale = Excluded.Keys.Where(route => !routes.Contains(route)).ToList();

		Assert.True(stale.Count == 0, "Excluded routes that do not exist: " + string.Join(", ", stale));
	}

	[Fact]
	public async Task NamedRoutes_Exist()
	{
		var routes = (await RoutesAsync(true)).ToHashSet();
		var documented = CommandCatalogFactory.Create().All.SelectMany(command => command.AllRoutes)
			.Select(route => CatchAll().Replace(route, "{$1}")).Distinct();

		var unknown = documented.Where(route => !routes.Contains(route)).ToList();

		Assert.True(unknown.Count == 0,
			"Commands that name a route that does not exist: " + string.Join(", ", unknown));
	}
}