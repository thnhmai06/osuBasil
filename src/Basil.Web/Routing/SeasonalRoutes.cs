using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

namespace Basil.Web.Routing;

/// <summary>
///     `/seasonals` — public read access to seasonal background images (already public via the osu!
///     client's own `GET osu.&lt;domain&gt;/web/osu-getseasonal.php`/`GET /seasonal/{fileName}` pair),
///     plus admin-key-gated writes with the same "no silent override" rule as `/faq`: `POST` only
///     creates a brand-new file (409 if the name is taken), `PUT` only replaces an existing one (404 if
///     it isn't). Backed by <see cref="SeasonalService" />, replacing the old admin-only `/seasonals`
///     surface.
/// </summary>
internal static class SeasonalRoutes
{
    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

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
                "(path-traversal-filtered). 409 if a file with that name already exists — use " +
                "`PUT /seasonals/{fileName}` to replace one." + AdminKeyNote)
            .WithTags("Seasonal Backgrounds")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
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
                "Content-Type is inferred from the file extension. Public.")
            .WithTags("Seasonal Backgrounds")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/seasonals/{fileName}", HandleReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("replaceSeasonalBackground")
            .WithSummary("Replace Seasonal Background")
            .WithDescription("Multipart upload, field name `file`. 404 if no file with this name exists yet " +
                "— use `POST /seasonals/` to create one." + AdminKeyNote)
            .WithTags("Seasonal Backgrounds")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/seasonals/{fileName}", (string fileName, SeasonalService seasonal) =>
            seasonal.Delete(fileName) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("deleteSeasonalBackground")
            .WithSummary("Delete Seasonal Background")
            .WithDescription("204 on success, 404 if the file doesn't exist." + AdminKeyNote)
            .WithTags("Seasonal Backgrounds")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HandleCreate(HttpContext context, SeasonalService seasonal,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

        await using var stream = file.OpenReadStream();
        var result = await seasonal.CreateAsync(file.FileName, stream, cancellationToken);
        return result == SeasonalService.CreateResult.AlreadyExists
            ? Results.Conflict(new ErrorResponse($"'{Path.GetFileName(file.FileName)}' already exists."))
            : Results.NoContent();
    }

    private static async Task<IResult> HandleReplace(string fileName, HttpContext context, SeasonalService seasonal,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

        await using var stream = file.OpenReadStream();
        var result = await seasonal.ReplaceAsync(fileName, stream, cancellationToken);
        return result == SeasonalService.ReplaceResult.NotFound ? Results.NotFound() : Results.NoContent();
    }
}
