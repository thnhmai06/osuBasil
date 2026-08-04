namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints that redirect short resource prefixes to their canonical paths.
/// </summary>
/// <remarks>
///     Each short prefix (`b`, `m`, `u`, `s`, `ss`) issues a 302 redirect to its canonical plural
///     resource path, preserving the remaining path and query string.
/// </remarks>
internal static class AbbreviationRedirectRoutes
{
	/// <summary>The prefix-to-target table the registered redirects are generated from.</summary>
	private static readonly (string Prefix, string Target)[] Map =
	[
		("b", "beatmapsets"),
		("m", "matches"),
		("u", "users"),
		("s", "scores"),
		("ss", "seasonals")
	];

	/// <summary>
	///     Registers a 302 redirect for every entry in <see cref="Map" />, from the short prefix to its
	///     canonical plural resource path. The trailing-catch-all variant carries the route name and
	///     summary used by the generated OpenAPI docs; the bare prefix redirect is excluded from them.
	/// </summary>
	/// <param name="group">The `api.` host route group to register the redirects on.</param>
	public static void MapAbbreviationRedirects(this RouteGroupBuilder group)
	{
		foreach (var (prefix, target) in Map)
		{
			group.MapGet($"/{prefix}", (HttpContext context) =>
					Results.Redirect($"/{target}{context.Request.QueryString}"))
				.WithGroupName("basilapi")
				.ExcludeFromDescription();

			var targetTitle = char.ToUpperInvariant(target[0]) + target[1..];
			// ReSharper disable once RouteTemplates.SyntaxError
			group.MapGet($"/{prefix}/{{**rest}}", (string rest, HttpContext context) =>
					Results.Redirect($"/{target}/{rest}{context.Request.QueryString}"))
				.WithGroupName("basilapi")
				.WithName($"redirectTo{targetTitle}")
				.WithSummary($"Redirect to /{target}.")
				.WithDescription($"Redirects to `/{target}/...`, preserving the remaining path and query string.")
				.WithTags("Abbreviation Redirects")
				.Produces(StatusCodes.Status302Found);
		}
	}
}