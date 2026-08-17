using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

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
		group.MapGet("/menu/seasonals", (MenuSeasonalService seasonal) => Results.Json(seasonal.ListFileNames()))
			.WithGroupName("basilapi")
			.WithName("listSeasonalBackgrounds")
			.WithSummary("List seasonal backgrounds.")
			.WithDescription("Returns the bare filenames, unlike the osu! client-facing " +
			                 "`GET osu.<domain>/web/osu-getseasonal.php`, which returns full URLs for the same " +
			                 "files.")
			.WithTags("Seasonal Backgrounds")
			.Produces<IReadOnlyList<string>>()
			.WithExample(StatusCodes.Status200OK, new List<string> { "winter-2026.png", "summer-2026.jpg" });

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
			.WithExample(StatusCodes.Status201Created, new MenuSeasonalCreatedView("winter-2026.png", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'winter-2026.png' already exists."));

		group.MapGet("/menu/seasonals/{fileName}", (string fileName, MenuSeasonalService seasonal) =>
			{
				var path = seasonal.FindFilePath(fileName);
				return path is null ? Results.NotFound() : Results.File(path, ContentTypes.Resolve(path));
			})
			.WithGroupName("basilapi")
			.WithName("downloadSeasonalBackground")
			.WithSummary("Download a seasonal background.")
			.WithDescription("""
			                 Serves the image file. `{fileName}` is the full filename including extension. Content-Type is taken from the file extension.

			                 Returns `404 Not Found` if the file doesn't exist.
			                 """)
			.WithTags("Seasonal Backgrounds")
			.ProducesProblem(StatusCodes.Status404NotFound);

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
			.WithExample(StatusCodes.Status200OK, new MenuSeasonalReplacedView("winter-2026.png", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/menu/seasonals/{fileName}", (string fileName, MenuSeasonalService seasonal,
				ILogger<MenuSeasonalRoutesLog> logger) =>
			{
				if (!seasonal.Delete(fileName)) return Results.NotFound();
				logger.LogDebug("Seasonal background deleted via admin API: FileName={FileName}", fileName);
				return Results.Json(new MenuSeasonalDeletedView(fileName, true));
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
			.WithExample(StatusCodes.Status200OK, new MenuSeasonalDeletedView("winter-2026.png", true))
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
		return Results.Created($"/menu/seasonals/{fileName}", new MenuSeasonalCreatedView(fileName, true));
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
		if (result == MenuSeasonalService.ReplaceResult.NotFound) return Results.NotFound();

		logger.LogDebug("Seasonal background replaced via admin API: FileName={FileName}", fileName);
		return Results.Json(new MenuSeasonalReplacedView(fileName, true));
	}

	/// <summary>Confirmation body for `DELETE /menu/seasonals/{fileName}`.</summary>
	public sealed record MenuSeasonalDeletedView(string FileName, bool Deleted);

	/// <summary>Confirmation body for `POST /menu/seasonals`.</summary>
	public sealed record MenuSeasonalCreatedView(string FileName, bool Created);

	/// <summary>Confirmation body for `PUT /menu/seasonals/{fileName}`.</summary>
	public sealed record MenuSeasonalReplacedView(string FileName, bool Replaced);
}
