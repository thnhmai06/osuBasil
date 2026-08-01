namespace Basil.Web.Routing;

/// <summary>
///     Short-prefix 302 redirects to the canonical plural resource paths: `b` for beatmapsets, `m`
///     for matches, `u` for users, `s` for scores, `ss` for seasonals. Preserves whatever path segment
///     and query string followed the prefix.
/// </summary>
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
			group.MapGet($"/{prefix}/{{**rest}}", (string rest, HttpContext context) =>
					Results.Redirect($"/{target}/{rest}{context.Request.QueryString}"))
				.WithGroupName("basilapi")
				.WithName($"redirectTo{targetTitle}")
				.WithSummary($"Redirect To {targetTitle}")
				.WithDescription($"302 redirect to `/{target}/...`, preserving the remaining path and query " +
				                 "string. Public.")
				.WithTags("Abbreviation Redirects")
				.Produces(StatusCodes.Status302Found);
		}
	}
}