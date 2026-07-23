using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

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
            .WithName("listFaqs")
            .WithSummary("List FAQs")
            .WithDescription("Bare entry names (no `.txt` suffix), matching `!faq list`'s own identifier " +
                "space. Public.")
            .WithTags("FAQ")
            .Produces<IReadOnlyList<string>>()
            .WithExample(StatusCodes.Status200OK, new List<string> { "rules", "schedule", "how-to-join" });

        group.MapPost("/faqs/", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("createFaq")
            .WithSummary("Create FAQ")
            .WithDescription("Multipart upload, field name `file`, must be a `.txt` file — its name (minus " +
                "the extension) becomes the entry's id. 409 if an entry with that name already exists — use " +
                "`PUT /faqs/{entry}` to replace one." + AdminKeyNote)
            .WithTags("FAQ")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Invalid entry name."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'rules' already exists."));

        group.MapGet("/faqs/{entry}", async (string entry, FaqService faq, CancellationToken cancellationToken) =>
        {
            var content = await faq.ReadEntryAsync(entry, cancellationToken);
            return content is null ? Results.NotFound() : Results.Text(content);
        })
            .WithGroupName("basilapi")
            .WithName("getFaq")
            .WithSummary("Get FAQ")
            .WithDescription("`{entry}` is the bare name used by `!faq <entry>` (no `.txt` suffix). 404 if no " +
                "entry with this name exists. Public.")
            .WithTags("FAQ")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/faqs/{entry}", HandleReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("replaceFaq")
            .WithSummary("Replace FAQ")
            .WithDescription("Multipart upload, field name `file`. 404 if no entry with this name exists yet " +
                "— use `POST /faqs/` to create one." + AdminKeyNote)
            .WithTags("FAQ")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Invalid entry name."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/faqs/{entry}", (string entry, FaqService faq) =>
            faq.DeleteEntry(entry) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("deleteFaq")
            .WithSummary("Delete FAQ")
            .WithDescription("204 on success, 404 if no entry with this name exists." + AdminKeyNote)
            .WithTags("FAQ")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> HandleCreate(HttpContext context, FaqService faq,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));
        if (!string.Equals(Path.GetExtension(file.FileName), ".txt", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new ErrorResponse("Only .txt uploads are accepted."));

        var entry = Path.GetFileNameWithoutExtension(file.FileName);
        await using var stream = file.OpenReadStream();
        var result = await faq.CreateEntryAsync(entry, stream, cancellationToken);
        return result switch
        {
            FaqService.CreateResult.AlreadyExists => Results.Conflict(new ErrorResponse($"'{entry}' already exists.")),
            FaqService.CreateResult.InvalidName => Results.BadRequest(new ErrorResponse("Invalid entry name.")),
            _ => Results.NoContent()
        };
    }

    private static async Task<IResult> HandleReplace(string entry, HttpContext context, FaqService faq,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

        await using var stream = file.OpenReadStream();
        var result = await faq.ReplaceEntryAsync(entry, stream, cancellationToken);
        return result switch
        {
            FaqService.ReplaceResult.NotFound => Results.NotFound(),
            FaqService.ReplaceResult.InvalidName => Results.BadRequest(new ErrorResponse("Invalid entry name.")),
            _ => Results.NoContent()
        };
    }
}
