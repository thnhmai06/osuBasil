using System.Text.Json.Serialization;
using Basil.Application.Services.Content;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Assets;

/// <summary>
///     Registers `GET /menu-content.json` on the `assets.` host: the osu! client's main-menu banner
///     manifest.
/// </summary>
/// <remarks>
///     Field names here are protocol-fixed (including the PascalCase <c>IsCurrent</c>, unlike every
///     other field), so each one carries an explicit <see cref="JsonPropertyNameAttribute" /> rather
///     than relying on the app's default camelCase naming policy.
/// </remarks>
internal static class MenuContentRoutes
{
	/// <summary>
	///     Registers the `/menu-content.json` route on the `assets.` host.
	/// </summary>
	/// <param name="group">The `assets.` host route group.</param>
	public static void MapMenuContentRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menu-content.json", async (MenuBannerService banners, CancellationToken cancellationToken) =>
			{
				var now = DateTime.UtcNow;
				var all = await banners.FetchAllAsync(cancellationToken);
				var images = all.Select(b => new MenuContentImage(
					banners.ResolveImageUrl(b.Image), b.Url, b.IsCurrent(now),
					b.Begins.ToString("O"), b.Expires.ToString("O")));

				return Results.Json(new MenuContentResponse([.. images]));
			})
			.WithGroupName("assets")
			.ExcludeFromDescription();
	}

	/// <summary>Response body for `GET /menu-content.json`.</summary>
	private sealed record MenuContentResponse([property: JsonPropertyName("images")] IReadOnlyList<MenuContentImage> Images);

	/// <summary>One entry in the `/menu-content.json` manifest.</summary>
	private sealed record MenuContentImage(
		[property: JsonPropertyName("image")] string Image,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("IsCurrent")] bool IsCurrent,
		[property: JsonPropertyName("begins")] string Begins,
		[property: JsonPropertyName("expires")] string Expires);
}
