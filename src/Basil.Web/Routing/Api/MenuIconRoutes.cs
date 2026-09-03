using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="MenuIconRoutes" /> class.</summary>
internal sealed class MenuIconRoutesLog;

/// <summary>
///     Registers the REST endpoints for querying and managing the in-game menu icon and its
///     click-through URL.
/// </summary>
/// <remarks>
///     The icon and its URL are publicly readable, while setting or deleting them require
///     administrator authorization. The icon image is either an uploaded file or an external URL —
///     never both at once; setting one form replaces the other. `PUT`/`PATCH /menu/icon` is a
///     multipart request carrying an `image` field that is either the uploaded file or a plain-text
///     external URL, told apart by how the field was sent.
/// </remarks>
internal static class MenuIconRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	private const string LoginEffectNote = " Takes effect for players who log in after this change. " +
	                                       "Already-connected sessions keep whatever menu icon they were sent at login.";

	private const string ImageFieldNote = " `image` is either an uploaded file (`.png`, `.jpg`, " +
	                                      "`.jpeg`, or `.gif`) or a plain-text external URL, told " +
	                                      "apart by how the field is sent.";

	/// <summary>
	///     Registers the `/menu/icon` read/write and `/menu/icon/image` delete routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMenuIconRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menu/icon", HandleGet)
			.WithGroupName("basilapi")
			.WithName("getMenuIcon")
			.WithSummary("Get menu icon.")
			.WithDescription(
				"Returns `{ image, url }`. `image` is a full URL — either the configured external URL, or this server's own `assets.<domain>/menu/icon` when an image was uploaded — or `null` if no icon is set. `url` is the click-through URL, or `null` if none is set.")
			.WithTags("Menu Icon")
			.Produces<MenuIconView>()
			.WithExample(StatusCodes.Status200OK,
				new MenuIconView("https://assets.example.com/menu/icon", "https://github.com/thnhmai06/osuBasil"));

		group.MapMethods("/menu/icon", [HttpMethods.Put, HttpMethods.Patch], HandleUpdate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateMenuIcon")
			.WithSummary("Update menu icon.")
			.WithDescription($"""
			                  Multipart request, fields `image`, `url` — each applied only if present. Sending `url` empty clears the click-through URL back to unset.{ImageFieldNote}
			                  """ + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon")
			.WithMultipartBody(
				MultipartField.FileOrText("image", false),
				MultipartField.Text("url", false))
			.Produces<MenuIconChangedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MenuIconChangedView($"Menu icon updated.{LoginEffectNote}"))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("'image' must be an uploaded file or start with http:// or https://."));

		group.MapDelete("/menu/icon", async (MenuIconService menuIcon, ILogger<MenuIconRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				await menuIcon.ResetAsync(cancellationToken);
				logger.LogInformation("Menu icon deleted via admin API");
				return Results.Json(new MenuIconChangedView($"Menu icon removed.{LoginEffectNote}"));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteMenuIcon")
			.WithSummary("Delete menu icon.")
			.WithDescription("Clears both `image` and `url`. Always returns `200 OK`, whether or not " +
			                 "either was set." + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon")
			.Produces<MenuIconChangedView>()
			.WithExample(StatusCodes.Status200OK,
				new MenuIconChangedView($"Menu icon removed.{LoginEffectNote}"));

		group.MapDelete("/menu/icon/image", async (MenuIconService menuIcon, ILogger<MenuIconRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				await menuIcon.DeleteIconAsync(cancellationToken);
				logger.LogInformation("Menu icon image deleted via admin API");
				return Results.Json(new MenuIconChangedView($"Menu icon image removed.{LoginEffectNote}"));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteMenuIconImage")
			.WithSummary("Delete menu icon image.")
			.WithDescription("Clears `image` only; `url` is left untouched. Always returns `200 OK`, " +
			                 "whether or not an image was set." + LoginEffectNote + AdminKeyNote)
			.WithTags("Menu Icon")
			.Produces<MenuIconChangedView>()
			.WithExample(StatusCodes.Status200OK,
				new MenuIconChangedView($"Menu icon image removed.{LoginEffectNote}"));
	}

	private static async Task<IResult> HandleGet(MenuIconService menuIcon, IOptions<ServerOptions> server,
		CancellationToken cancellationToken)
	{
		var path = await menuIcon.GetPathAsync(cancellationToken);
		var image = path switch
		{
			null => null,
			_ when MenuIconService.IsExternalUrl(path) => path,
			_ => $"https://assets.{server.Value.Domain}/menu/icon"
		};
		var url = await menuIcon.ReadUrlAsync(cancellationToken)
		          ?? (path is not null ? "https://github.com/thnhmai06/osuBasil" : null);

		return Results.Json(new MenuIconView(image, url));
	}

	private static async Task<IResult> HandleUpdate(HttpContext context, MenuIconService menuIcon,
		ILogger<MenuIconRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart form."));

		var form = await context.Request.ReadFormAsync(cancellationToken);

		var file = form.Files.GetFile("image");
		if (file is not null)
		{
			var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
			if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif"))
				return Results.BadRequest(new ErrorResponse("Only .png/.jpg/.jpeg/.gif uploads are accepted."));

			await using var stream = file.OpenReadStream();
			await menuIcon.SaveIconAsync(stream, extension, cancellationToken);
		}
		else
		{
			var imageRaw = form["image"].ToString();
			if (!string.IsNullOrEmpty(imageRaw))
			{
				if (!MenuIconService.IsExternalUrl(imageRaw))
					return Results.BadRequest(
						new ErrorResponse("'image' must be an uploaded file or start with http:// or https://."));
				await menuIcon.SetExternalIconAsync(imageRaw, cancellationToken);
			}
		}

		// A present-but-empty `url` field is a deliberate reset to unset, told apart from an absent
		// field (which leaves the current URL untouched) by presence in the form, not by content.
		if (form.TryGetValue("url", out var urlValues))
			await menuIcon.SaveUrlAsync(urlValues.ToString(), cancellationToken);

		logger.LogInformation("Menu icon updated via admin API");
		return Results.Json(new MenuIconChangedView($"Menu icon updated.{LoginEffectNote}"));
	}

	/// <summary>Response body for `GET /menu/icon`.</summary>
	public sealed record MenuIconView(string? Image, string? Url);

	/// <summary>Confirmation body for the menu icon write routes.</summary>
	public sealed record MenuIconChangedView(string Message);
}