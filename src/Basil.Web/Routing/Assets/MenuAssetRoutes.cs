using System.Text.Json.Serialization;
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

		group.MapGet("/menu-content.json", async (MenuBannerService banners, CancellationToken cancellationToken) =>
			{
				var now = DateTime.UtcNow;
				var all = await banners.FetchAllAsync(cancellationToken);
				var images = all.Select(b => new MenuContentImage(
					banners.ResolveSourceUrl(b.Source), b.Url, b.IsCurrent(now),
					b.Begins?.ToString("O"), b.Expires?.ToString("O")));

				return Results.Json(new MenuContentResponse([.. images]));
			})
			.WithGroupName("assets")
			.ExcludeFromDescription();
	}

	// <summary>Response body for `GET /menu-content.json`.</summary>
	private sealed record MenuContentResponse(
		[property: JsonPropertyName("images")] IReadOnlyList<MenuContentImage> Images);

	/// <summary>
	///     One entry in the `/menu-content.json` manifest. <c>begins</c>/<c>expires</c> are
	///     <see langword="null" /> when the banner has no lower/upper display-window bound.
	/// </summary>
	private sealed record MenuContentImage(
		[property: JsonPropertyName("image")] string Image,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("IsCurrent")]
		bool IsCurrent,
		[property: JsonPropertyName("begins")] string? Begins,
		[property: JsonPropertyName("expires")]
		string? Expires);
}