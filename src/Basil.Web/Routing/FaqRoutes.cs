using Basil.Application.Services.Content;
using Basil.Web.Auth;

namespace Basil.Web.Routing;

/// <summary>
///     `/faqs` — public read access to the same FAQ entries `!faq` serves in chat, plus admin-key-gated
///     writes with a "no silent override" rule: `POST` only creates a brand-new entry (409 if the name
///     is already taken), `PUT` only replaces an existing one (404 if it isn't). Backed by
///     <see cref="FaqService" />, shared with <see cref="Basil.Application.Services.Bot.CommandDispatcher" />.
/// </summary>
internal static class FaqRoutes
{
    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    public static void MapFaqRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/faqs/", (FaqService faq) => Results.Json(faq.ListEntries()))
            .WithGroupName("basilapi")
            .WithSummary("List FAQ entry names.")
            .WithDescription("Bare entry names (no `.txt` suffix), matching `!faq list`'s own identifier " +
                "space. Public.")
            .WithTags("FAQ");

        group.MapPost("/faqs/", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Create a new FAQ entry.")
            .WithDescription("Multipart upload, field name `file`, must be a `.txt` file — its name (minus " +
                "the extension) becomes the entry's id. 409 if an entry with that name already exists — use " +
                "`PUT /faqs/{entry}` to replace one." + AdminKeyNote)
            .WithTags("FAQ");

        group.MapGet("/faqs/{entry}", async (string entry, FaqService faq, CancellationToken cancellationToken) =>
        {
            var content = await faq.ReadEntryAsync(entry, cancellationToken);
            return content is null ? Results.NotFound() : Results.Text(content);
        })
            .WithGroupName("basilapi")
            .WithSummary("Get one FAQ entry's raw text, by entry name.")
            .WithDescription("`{entry}` is the bare name used by `!faq <entry>` (no `.txt` suffix). 404 if no " +
                "entry with this name exists. Public.")
            .WithTags("FAQ");

        group.MapPut("/faqs/{entry}", HandleReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace an existing FAQ entry's content.")
            .WithDescription("Multipart upload, field name `file`. 404 if no entry with this name exists yet " +
                "— use `POST /faqs/` to create one." + AdminKeyNote)
            .WithTags("FAQ");

        group.MapDelete("/faqs/{entry}", (string entry, FaqService faq) =>
            faq.DeleteEntry(entry) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Delete a FAQ entry.")
            .WithDescription("204 on success, 404 if no entry with this name exists." + AdminKeyNote)
            .WithTags("FAQ");
    }

    private static async Task<IResult> HandleCreate(HttpContext context, FaqService faq,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest("Missing 'file' form field.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only .txt uploads are accepted.");

        var entry = Path.GetFileNameWithoutExtension(file.FileName);
        await using var stream = file.OpenReadStream();
        var result = await faq.CreateEntryAsync(entry, stream, cancellationToken);
        return result switch
        {
            FaqService.CreateResult.AlreadyExists => Results.Conflict(new { error = $"'{entry}' already exists." }),
            FaqService.CreateResult.InvalidName => Results.BadRequest(new { error = "Invalid entry name." }),
            _ => Results.NoContent()
        };
    }

    private static async Task<IResult> HandleReplace(string entry, HttpContext context, FaqService faq,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest("Missing 'file' form field.");

        await using var stream = file.OpenReadStream();
        var result = await faq.ReplaceEntryAsync(entry, stream, cancellationToken);
        return result switch
        {
            FaqService.ReplaceResult.NotFound => Results.NotFound(),
            FaqService.ReplaceResult.InvalidName => Results.BadRequest(new { error = "Invalid entry name." }),
            _ => Results.NoContent()
        };
    }
}
