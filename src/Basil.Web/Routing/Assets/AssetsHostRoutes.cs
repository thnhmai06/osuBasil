using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Web.Routing.Assets;

/// <summary>
///     Registers the `assets.` host's REST endpoints.
/// </summary>
/// <remarks>
///     The `assets.` host is Basil's internal CDN-equivalent: menu banner/icon/seasonal images,
///     beatmapset covers, and other media served through ImageSharp.Web instead of `api.`'s
///     hand-written file routes.
/// </remarks>
internal static class AssetsHostRoutes
{
	/// <summary>
	///     Registers the `assets.{domain}` host's routes.
	/// </summary>
	/// <param name="group">The `assets.{domain}` route group.</param>
	public static void MapAssetsGroup(this RouteGroupBuilder group)
	{
		group.MapGet("/health", () => Results.Json(new HealthStatus("ok")))
			.WithGroupName("assets")
			.WithName("getAssetsHealth")
			.WithSummary("Get health.")
			.WithDescription("Returns `{ status: \"ok\" }` while the server is up.")
			.WithTags("Health")
			.Produces<HealthStatus>()
			.WithExample(StatusCodes.Status200OK, new HealthStatus("ok"));
	}

	private sealed record HealthStatus(string Status);
}
