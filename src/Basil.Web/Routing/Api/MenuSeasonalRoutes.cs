using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="MenuSeasonalRoutes" /> class.</summary>
internal sealed class MenuSeasonalRoutesLog;

/// <summary>
///     Registers the REST endpoints for listing, querying, and managing seasonal backgrounds.
/// </summary>
/// <remarks>
///     Seasonal backgrounds are publicly readable, while creating, replacing, and deleting them
///     require administrator authorization. Creating a file never overwrites an existing one, and
///     replacing one requires it to already exist.
/// </remarks>
internal static class MenuSeasonalRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/menu/seasonals` read and admin-key-gated write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMenuSeasonalRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menu/seasonals", (IOptions<ServerOptions> server) =>
				Results.Redirect($"https://assets.{server.Value.Domain}/menu/seasonals"))
			.WithGroupName("basilapi")
			.WithName("listSeasonalBackgrounds")
			.WithSummary("List seasonal backgrounds.")
			.WithDescription("Redirects to `GET assets.<domain>/menu/seasonals`, which returns the bare " +
			                 "filenames.")
			.WithTags("Seasonal Backgrounds")
			.Produces(StatusCodes.Status302Found);

		group.MapPost("/menu/seasonals", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createSeasonalBackground")
			.WithSummary("Create a seasonal background.")
			.WithDescription("""
			                 Multipart upload, field name `file`, saved under its own uploaded filename.

			                 Returns `409 Conflict` if a file with that name already exists; use `PUT /menu/seasonals/{fileName}` to replace one.
			                 """ + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.WithMultipartFileUpload()
			.Produces<MenuSeasonalCreatedView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status201Created, new MenuSeasonalCreatedView("winter-2026.png"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'winter-2026.png' already exists."));

		group.MapGet("/menu/seasonals/{fileName}", (string fileName, IOptions<ServerOptions> server) =>
				Results.Redirect($"https://assets.{server.Value.Domain}/menu/seasonals/{fileName}"))
			.WithGroupName("basilapi")
			.WithName("downloadSeasonalBackground")
			.WithSummary("Download a seasonal background.")
			.WithDescription("Redirects to `GET assets.<domain>/menu/seasonals/{fileName}`, which serves " +
			                 "the image file.")
			.WithTags("Seasonal Backgrounds")
			.Produces(StatusCodes.Status302Found);

		group.MapPut("/menu/seasonals/{fileName}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceSeasonalBackground")
			.WithSummary("Replace a seasonal background.")
			.WithDescription("""
			                 Multipart upload, field name `file`. Replaces the file's contents; the filename is fixed by the URL.

			                 Returns `404 Not Found` if no file with this name exists yet; use `POST /menu/seasonals` to create one.
			                 """ + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.WithMultipartFileUpload()
			.Produces<MenuSeasonalReplacedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new MenuSeasonalReplacedView("winter-2026.png"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/menu/seasonals/{fileName}", HandleRename)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("renameSeasonalBackground")
			.WithSummary("Rename a seasonal background.")
			.WithDescription("""
			                 Renames the file, keeping its content unchanged.

			                 Returns `404 Not Found` if no file with this name exists, or `409 Conflict` if a file with the new name already exists.
			                 """ + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.Produces<MenuSeasonalRenamedView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MenuSeasonalRenamedView("winter-2026.png", "winter-final.png"))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'winter-final.png' already exists."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/menu/seasonals/{fileName}", (string fileName, MenuSeasonalService seasonal,
				ILogger<MenuSeasonalRoutesLog> logger) =>
			{
				if (!seasonal.Delete(fileName))
					return Results.NotFound(new ErrorResponse("Seasonal background not found."));
				logger.LogDebug("Seasonal background deleted via admin API: FileName={FileName}", fileName);
				return Results.Json(new MenuSeasonalDeletedView(fileName));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteSeasonalBackground")
			.WithSummary("Delete a seasonal background.")
			.WithDescription("""
			                 Deletes the file and returns a confirmation body.

			                 Returns `404 Not Found` if the file doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.Produces<MenuSeasonalDeletedView>()
			.WithExample(StatusCodes.Status200OK, new MenuSeasonalDeletedView("winter-2026.png"))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleCreate(HttpContext context, MenuSeasonalService seasonal,
		ILogger<MenuSeasonalRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		var fileName = Path.GetFileName(file.FileName);
		await using var stream = file.OpenReadStream();
		var result = await seasonal.CreateAsync(file.FileName, stream, cancellationToken);
		if (result == MenuSeasonalService.CreateResult.AlreadyExists)
			return Results.Conflict(new ErrorResponse($"'{fileName}' already exists."));

		logger.LogDebug("Seasonal background created via admin API: FileName={FileName}", fileName);
		return Results.Created($"/menu/seasonals/{fileName}", new MenuSeasonalCreatedView(fileName));
	}

	private static async Task<IResult> HandleReplace(string fileName, HttpContext context,
		MenuSeasonalService seasonal, ILogger<MenuSeasonalRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		await using var stream = file.OpenReadStream();
		var result = await seasonal.ReplaceAsync(fileName, stream, cancellationToken);
		if (result == MenuSeasonalService.ReplaceResult.NotFound)
			return Results.NotFound(new ErrorResponse("Seasonal background not found."));

		logger.LogDebug("Seasonal background replaced via admin API: FileName={FileName}", fileName);
		return Results.Json(new MenuSeasonalReplacedView(fileName));
	}

	private static IResult HandleRename(string fileName, RenameSeasonalBackgroundRequest request,
		MenuSeasonalService seasonal, ILogger<MenuSeasonalRoutesLog> logger)
	{
		var newFileName = Path.GetFileName(request.NewFileName);
		var result = seasonal.Rename(fileName, newFileName);
		if (result == MenuSeasonalService.RenameResult.NotFound)
			return Results.NotFound(new ErrorResponse("Seasonal background not found."));
		if (result == MenuSeasonalService.RenameResult.TargetAlreadyExists)
			return Results.Conflict(new ErrorResponse($"'{newFileName}' already exists."));

		logger.LogDebug("Seasonal background renamed via admin API: FileName={FileName}, NewFileName={NewFileName}",
			fileName, newFileName);
		return Results.Json(new MenuSeasonalRenamedView(fileName, newFileName));
	}

	/// <summary>Confirmation body for `DELETE /menu/seasonals/{fileName}`.</summary>
	public sealed record MenuSeasonalDeletedView(string FileName);

	/// <summary>Confirmation body for `POST /menu/seasonals`.</summary>
	public sealed record MenuSeasonalCreatedView(string FileName);

	/// <summary>Confirmation body for `PUT /menu/seasonals/{fileName}`.</summary>
	public sealed record MenuSeasonalReplacedView(string FileName);

	/// <summary>Confirmation body for `PATCH /menu/seasonals/{fileName}`.</summary>
	public sealed record MenuSeasonalRenamedView(string FileName, string NewFileName);

	/// <summary>Request body for `PATCH /menu/seasonals/{fileName}`.</summary>
	/// <param name="NewFileName">The file name to rename the seasonal background to.</param>
	public sealed record RenameSeasonalBackgroundRequest(string NewFileName);
}