using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

namespace Basil.Web.Routing;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="SeasonalRoutes" /> is static and can't be
///     a type argument.
/// </summary>
internal sealed class SeasonalRoutesLog;

/// <summary>
///     `/seasonals`: public read access to seasonal background images (already public via the osu!
///     client's own `GET osu.&lt;domain&gt;/web/osu-getseasonal.php`/`GET /seasonal/{fileName}` pair),
///     plus admin-key-gated writes with the same "no silent override" rule as `/faq`: `POST` only
///     creates a brand-new file (409 if the name is taken), `PUT` only replaces an existing one (404 if
///     it isn't). Backed by <see cref="SeasonalService" />, replacing the old admin-only `/seasonals`
///     surface.
/// </summary>
internal static class SeasonalRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/seasonals` read and admin-key-gated write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapSeasonalRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/seasonals/", (SeasonalService seasonal) => Results.Json(seasonal.ListFileNames()))
			.WithGroupName("basilapi")
			.WithName("listSeasonalBackgrounds")
			.WithSummary("List Seasonal Backgrounds")
			.WithDescription("Bare filenames (unlike the osu! client-facing " +
			                 "`GET osu.<domain>/web/osu-getseasonal.php`, which returns full URLs for the same folder). " +
			                 "Public.")
			.WithTags("Seasonal Backgrounds")
			.Produces<IReadOnlyList<string>>()
			.WithExample(StatusCodes.Status200OK, new List<string> { "winter-2026.png", "summer-2026.jpg" });

		group.MapPost("/seasonals/", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createSeasonalBackground")
			.WithSummary("Create Seasonal Background")
			.WithDescription("Multipart upload, field name `file`, saved under its own uploaded filename " +
			                 "(path-traversal-filtered). 409 if a file with that name already exists. Use " +
			                 "`PUT /seasonals/{fileName}` to replace one." + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.WithMultipartFileUpload()
			.Produces<SeasonalCreatedView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status201Created, new SeasonalCreatedView("winter-2026.png", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'winter-2026.png' already exists."));

		group.MapGet("/seasonals/{fileName}", (string fileName, SeasonalService seasonal) =>
			{
				var path = seasonal.FindFilePath(fileName);
				if (path is null) return Results.NotFound();

				var contentType = Path.GetExtension(path).ToLowerInvariant() switch
				{
					".png" => "image/png",
					".jpg" or ".jpeg" => "image/jpeg",
					".gif" => "image/gif",
					_ => "application/octet-stream"
				};
				return Results.File(path, contentType);
			})
			.WithGroupName("basilapi")
			.WithName("downloadSeasonalBackground")
			.WithSummary("Download Seasonal Background")
			.WithDescription("`{fileName}` is the full filename including extension. 404 if it doesn't exist. " +
			                 "Content-Type is taken from the file extension. Public.")
			.WithTags("Seasonal Backgrounds")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/seasonals/{fileName}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceSeasonalBackground")
			.WithSummary("Replace Seasonal Background")
			.WithDescription("Multipart upload, field name `file`. 404 if no file with this name exists yet. " +
			                 "Use `POST /seasonals/` to create one." + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.WithMultipartFileUpload()
			.Produces<SeasonalReplacedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new SeasonalReplacedView("winter-2026.png", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/seasonals/{fileName}", (string fileName, SeasonalService seasonal,
				ILogger<SeasonalRoutesLog> logger) =>
			{
				if (!seasonal.Delete(fileName)) return Results.NotFound();
				logger.LogDebug("Seasonal background deleted via admin API: FileName={FileName}", fileName);
				return Results.Json(new SeasonalDeletedView(fileName, true));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteSeasonalBackground")
			.WithSummary("Delete Seasonal Background")
			.WithDescription("Returns a confirmation body. 404 if the file doesn't exist." + AdminKeyNote)
			.WithTags("Seasonal Backgrounds")
			.Produces<SeasonalDeletedView>()
			.WithExample(StatusCodes.Status200OK, new SeasonalDeletedView("winter-2026.png", true))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleCreate(HttpContext context, SeasonalService seasonal,
		ILogger<SeasonalRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		var fileName = Path.GetFileName(file.FileName);
		await using var stream = file.OpenReadStream();
		var result = await seasonal.CreateAsync(file.FileName, stream, cancellationToken);
		if (result == SeasonalService.CreateResult.AlreadyExists)
			return Results.Conflict(new ErrorResponse($"'{fileName}' already exists."));

		logger.LogDebug("Seasonal background created via admin API: FileName={FileName}", fileName);
		return Results.Created($"/seasonals/{fileName}", new SeasonalCreatedView(fileName, true));
	}

	private static async Task<IResult> HandleReplace(string fileName, HttpContext context, SeasonalService seasonal,
		ILogger<SeasonalRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		await using var stream = file.OpenReadStream();
		var result = await seasonal.ReplaceAsync(fileName, stream, cancellationToken);
		if (result == SeasonalService.ReplaceResult.NotFound) return Results.NotFound();

		logger.LogDebug("Seasonal background replaced via admin API: FileName={FileName}", fileName);
		return Results.Json(new SeasonalReplacedView(fileName, true));
	}

	/// <summary>Confirmation body for `DELETE /seasonals/{fileName}`.</summary>
	public sealed record SeasonalDeletedView(string FileName, bool Deleted);

	/// <summary>Confirmation body for `POST /seasonals/`.</summary>
	public sealed record SeasonalCreatedView(string FileName, bool Created);

	/// <summary>Confirmation body for `PUT /seasonals/{fileName}`.</summary>
	public sealed record SeasonalReplacedView(string FileName, bool Replaced);
}