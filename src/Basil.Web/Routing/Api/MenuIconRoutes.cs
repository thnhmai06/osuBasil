using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="MenuIconRoutes" /> is static and can't be
///     a type argument.
/// </summary>
internal sealed class MenuIconRoutesLog;

/// <summary>
///     Registers the REST endpoints for querying and managing the in-game menu icon and its
///     click-through URL.
/// </summary>
/// <remarks>
///     The icon and its URL are publicly readable, while setting or deleting them require
///     administrator authorization. Both are singletons: setting one replaces whatever was set before.
///     The icon is either an uploaded image file or an external URL — never both at once; setting one
///     form replaces the other.
/// </remarks>
internal static class MenuIconRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	private const string LoginEffectNote = " Takes effect for players who log in after this change. " +
	                                       "Already-connected sessions keep whatever menu icon they were sent at login.";

	/// <summary>
	///     Registers the `/menuicon` image and URL read/write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMenuIconRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menuicon/icon", async (MenuIconService menuIcon, CancellationToken cancellationToken) =>
			{
				var path = await menuIcon.GetPathAsync(cancellationToken);
				if (path is null) return Results.NotFound();

				if (MenuIconService.IsExternalUrl(path)) return Results.Redirect(path);
				return File.Exists(path) ? Results.File(path, ContentTypes.Resolve(path)) : Results.NotFound();
			})
			.WithGroupName("basilapi")
			.WithName("getMenuIcon")
			.WithSummary("Get menu icon.")
			.WithDescription("""
			                 Serves the in-game main menu icon image. Content-Type is taken from the file extension.

			                 If the icon is currently set to an external URL, redirects there instead of serving
			                 a file. Returns `404 Not Found` if no icon is set.
			                 """)
			.WithTags("Menu Icon Image")
			.Produces(StatusCodes.Status302Found)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/menuicon/icon", HandleReplaceIcon)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMenuIcon")
			.WithSummary("Set menu icon from an upload.")
			.WithDescription("Multipart upload, field name `file`, must be `.png`, `.jpg`, `.jpeg`, or `.gif`. " +
			                 "Replaces whatever icon is currently set (uploaded file or external URL)." +
			                 LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon Image")
			.WithMultipartFileUpload()
			.Produces<MenuIconChangedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MenuIconChangedView(true, $"Menu icon updated.{LoginEffectNote}"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."));

		group.MapPatch("/menuicon/icon", HandleSetExternalIcon)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMenuIconExternalUrl")
			.WithSummary("Set menu icon to an external image URL.")
			.WithDescription("Body: `{ url }`. Replaces whatever icon is currently set (uploaded file or external " +
			                 "URL) with a reference to an externally hosted image; nothing is uploaded to this " +
			                 "server." + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon Image")
			.Produces<MenuIconChangedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MenuIconChangedView(true, $"Menu icon updated.{LoginEffectNote}"))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("URL must start with http:// or https://."));

		group.MapDelete("/menuicon/icon", async (MenuIconService menuIcon, ILogger<MenuIconRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				await menuIcon.DeleteIconAsync(cancellationToken);
				logger.LogInformation("Menu icon deleted via admin API");
				return Results.Json(new MenuIconChangedView(true,
					$"Menu icon removed.{LoginEffectNote}"));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteMenuIcon")
			.WithSummary("Delete menu icon.")
			.WithDescription("Turns the menu icon off entirely. Always returns `200 OK`, whether or not one was " +
			                 "set." + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon Image")
			.Produces<MenuIconChangedView>()
			.WithExample(StatusCodes.Status200OK,
				new MenuIconChangedView(true, $"Menu icon removed.{LoginEffectNote}"));

		group.MapGet("/menuicon/url", async (MenuIconService menuIcon, CancellationToken cancellationToken) =>
			{
				var url = await menuIcon.ReadUrlAsync(cancellationToken)
				          ?? (await menuIcon.GetPathAsync(cancellationToken) is not null
					          ? "https://github.com/thnhmai06/osuBasil"
					          : null);
				return Results.Json(new MenuIconUrlView(url));
			})
			.WithGroupName("basilapi")
			.WithName("getMenuIconUrl")
			.WithSummary("Get menu icon URL.")
			.WithDescription("""
			                 Returns the menu icon's click-through URL, or `null` if no menu icon is set.

			                 If one is set but no URL was explicitly configured, this returns a default (`https://github.com/thnhmai06/osuBasil`).
			                 """)
			.WithTags("Menu Icon URL")
			.Produces<MenuIconUrlView>()
			.WithExample(StatusCodes.Status200OK, new MenuIconUrlView("https://github.com/thnhmai06/osuBasil"));

		group.MapPut("/menuicon/url", async (MenuIconUrlBody body, MenuIconService menuIcon,
				ILogger<MenuIconRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				await menuIcon.SaveUrlAsync(body.Url, cancellationToken);
				logger.LogInformation("Menu icon URL updated via admin API");
				return Results.Json(new MenuIconChangedView(true, $"Menu icon URL updated.{LoginEffectNote}"));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setMenuIconUrl")
			.WithSummary("Set menu icon URL.")
			.WithDescription("Body: `{ url }`. Replaces whatever URL is currently set, or sets one if none is " +
			                 "set. There is no DELETE; to fall back to the default, set the URL to it explicitly " +
			                 "(`https://github.com/thnhmai06/osuBasil`)." + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon URL")
			.Produces<MenuIconChangedView>()
			.WithExample(StatusCodes.Status200OK,
				new MenuIconChangedView(true, $"Menu icon URL updated.{LoginEffectNote}"));
	}

	private static async Task<IResult> HandleReplaceIcon(HttpContext context, MenuIconService menuIcon,
		ILogger<MenuIconRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
			return Results.BadRequest(new ErrorResponse("Only .png/.jpg/.jpeg/.gif uploads are accepted."));

		await using var stream = file.OpenReadStream();
		await menuIcon.SaveIconAsync(stream, extension, cancellationToken);
		logger.LogInformation("Menu icon updated via admin API");
		return Results.Json(new MenuIconChangedView(true, $"Menu icon updated.{LoginEffectNote}"));
	}

	private static async Task<IResult> HandleSetExternalIcon(MenuIconUrlBody body, MenuIconService menuIcon,
		ILogger<MenuIconRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!MenuIconService.IsExternalUrl(body.Url))
			return Results.BadRequest(new ErrorResponse("URL must start with http:// or https://."));

		await menuIcon.SetExternalIconAsync(body.Url, cancellationToken);
		logger.LogInformation("Menu icon set to external URL via admin API");
		return Results.Json(new MenuIconChangedView(true, $"Menu icon updated.{LoginEffectNote}"));
	}

	/// <summary>Confirmation body for the menu icon/url write routes.</summary>
	public sealed record MenuIconChangedView(bool Success, string Message);

	/// <summary>Response body for `GET /menuicon/url`.</summary>
	public sealed record MenuIconUrlView(string? Url);

	/// <summary>Request body for `PUT /menuicon/url` and `PATCH /menuicon/icon`.</summary>
	public sealed record MenuIconUrlBody(string Url);
}