using Basil.Web.OpenApi;
using Scalar.AspNetCore;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the `api.` host's REST endpoints.
/// </summary>
/// <remarks>
///     The `api.` host serves the match report and per-resource live streams, file downloads,
///     admin-key-gated management CRUD, a `/health` probe, the abbreviation redirects, and the
///     generated OpenAPI/Scalar documentation site.
/// </remarks>
internal static class ApiHostRoutes
{
	/// <summary>
	///     Registers the `api.{domain}` host's routes.
	/// </summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapApiGroup(this RouteGroupBuilder group)
	{
		group.MapOpenApi().ExcludeFromDescription();

		group.MapGet("/", () => Results.Redirect("/docs/")).ExcludeFromDescription();

		group.MapGet("/health", () => Results.Json(new HealthStatus("ok")))
			.WithGroupName("basilapi")
			.WithName("getHealth")
			.WithSummary("Get health.")
			.WithDescription("Returns `{ status: \"ok\" }` while the server is up.")
			.WithTags("Health")
			.Produces<HealthStatus>()
			.WithExample(StatusCodes.Status200OK, new HealthStatus("ok"));

		var docsSiteRoot = Path.Combine(AppContext.BaseDirectory, "docs-site");
		var docs = group.MapGroup("/docs");

		docs.MapGet("/", () => Results.File(Path.Combine(docsSiteRoot, "index.html"), "text/html"))
			.ExcludeFromDescription();

		docs.MapGet("/icon.png", () => Results.File(Path.Combine(docsSiteRoot, "icon.png"), "image/png"))
			.ExcludeFromDescription();

		group.MapGet("/favicon.ico", () => Results.Redirect("/docs/icon.png")).ExcludeFromDescription();

		docs.MapGet("/osu-client/",
				() => Results.File(Path.Combine(docsSiteRoot, "osu-client", "index.html"), "text/html"))
			.ExcludeFromDescription();

		docs.MapScalarApiReference("/basil-api/", options =>
		{
			options.Title = "Basil API";
			options.Favicon = "/docs/icon.png";
			options.AddDocument("basilapi", "Basil API");
		}).ExcludeFromDescription();

		docs.MapGet("/basil-bot/",
				() => Results.File(Path.Combine(docsSiteRoot, "basil-bot", "index.html"), "text/html"))
			.ExcludeFromDescription();

		docs.MapGet("/irc-client/",
				() => Results.File(Path.Combine(docsSiteRoot, "irc-client", "index.html"), "text/html"))
			.ExcludeFromDescription();

		group.MapMatchRoutes();

		group.MapUserRoutes();

		group.MapScoreRoutes();

		group.MapBeatmapsetRoutes();

		group.MapFaqRoutes();

		group.MapSeasonalRoutes();

		group.MapMenuIconRoutes();

		group.MapAdminKeyRoutes();

		group.MapAnnounceRoutes();

		group.MapAbbreviationRedirects();
	}

	private sealed record HealthStatus(string Status);
}