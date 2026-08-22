using Basil.Application.Services.Content;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Web.Routing.Assets;

/// <summary>
///     Registers the non-image `/menu/...` routes on the `assets.` host: the seasonal background
///     listing, and the menu icon's external-URL/not-set fallback.
/// </summary>
/// <remarks>
///     The image requests themselves (`/menu/seasonals/{fileName}`, `/menu/banners/{fileName}`, and —
///     when it's an uploaded file — `/menu/icon`) never reach here: ImageSharp.Web's providers resolve
///     them earlier in the pipeline. See <see cref="AssetsHostRoutes" />.
/// </remarks>
internal static class MenuAssetRoutes
{
	/// <summary>
	///     Registers the `/menu/seasonals` listing and `/menu/icon` fallback routes on the `assets.` host.
	/// </summary>
	/// <param name="group">The `assets.` host route group.</param>
	public static void MapMenuAssetRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menu/seasonals", (MenuSeasonalService seasonal) => Results.Json(seasonal.ListFileNames()))
			.WithGroupName("assets")
			.WithName("listMenuSeasonals")
			.WithSummary("List seasonal backgrounds.")
			.WithDescription("Returns the bare filenames. Fetch an individual image at " +
			                 "`GET /menu/seasonals/{fileName}` on this same host.")
			.WithTags("Menu")
			.Produces<IReadOnlyList<string>>()
			.WithExample(StatusCodes.Status200OK, new List<string> { "winter-2026.png", "summer-2026.jpg" });

		group.MapGet("/menu/icon", async (MenuIconService menuIcon, CancellationToken cancellationToken) =>
			{
				var path = await menuIcon.GetPathAsync(cancellationToken);
				return path is not null && MenuIconService.IsExternalUrl(path)
					? Results.Redirect(path)
					: Results.NotFound();
			})
			.WithGroupName("assets")
			.WithName("getMenuIconImage")
			.WithSummary("Get menu icon image.")
			.WithDescription("Serves the icon image directly when it was uploaded to this server. " +
			                 "Redirects when it's set to an external URL. Returns `404 Not Found` when " +
			                 "no icon is set.")
			.WithTags("Menu")
			.Produces(StatusCodes.Status302Found)
			.ProducesProblem(StatusCodes.Status404NotFound);
	}
}