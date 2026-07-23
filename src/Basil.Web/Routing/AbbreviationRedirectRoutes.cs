namespace Basil.Web.Routing;

/// <summary>
///     Short-prefix 302 redirects to the canonical plural resource paths — `b` for beatmapsets, `m`
///     for matches, `u` for users, `s` for scores, `ss` for seasonals. Preserves whatever path segment
///     and query string followed the prefix.
/// </summary>
internal static class AbbreviationRedirectRoutes
{
    private static readonly (string Prefix, string Target)[] Map =
    [
        ("b", "beatmapsets"),
        ("m", "matches"),
        ("u", "users"),
        ("s", "scores"),
        ("ss", "seasonals")
    ];

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
