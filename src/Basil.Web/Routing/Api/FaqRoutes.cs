using Basil.Application.Services.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="FaqRoutes" /> class.</summary>
internal sealed class FaqRoutesLog;

/// <summary>
///     Registers the REST endpoints for querying and managing FAQ entries.
/// </summary>
/// <remarks>
///     FAQ entries are publicly readable, while creating, updating, and deleting entries require
///     administrator authorization. Creating an entry never overwrites an existing one, and
///     updating an entry requires it to already exist.
/// </remarks>
internal static class FaqRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/faqs` read and admin-key-gated write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapFaqRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/faqs/", (FaqService faq) => Results.Json(faq.ListEntries()))
			.WithGroupName("basilapi")
			.WithName("listFaqs")
			.WithSummary("List FAQs.")
			.WithDescription("Returns the bare entry names, matching the same identifiers `!faq` uses in chat.")
			.WithTags("FAQ")
			.Produces<IReadOnlyList<string>>()
			.WithExample(StatusCodes.Status200OK, new List<string> { "rules", "schedule", "how-to-join" });

		group.MapPost("/faqs/", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createFaq")
			.WithSummary("Create an FAQ.")
			.WithDescription("""
			                 Multipart upload, field name `file`, must be a `text/plain*` file. The uploaded filename, minus its extension, becomes the entry's id.

			                 Returns `409 Conflict` if an entry with that name already exists; use `PUT /faqs/{entry}` to replace one.
			                 """ + AdminKeyNote)
			.WithTags("FAQ")
			.WithMultipartFileUpload()
			.Produces<FaqCreatedView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status201Created, new FaqCreatedView("rules", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Invalid entry name."))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("'rules' already exists."));

		group.MapGet("/faqs/{entry}", async (string entry, FaqService faq, CancellationToken cancellationToken) =>
			{
				var content = await faq.ReadEntryAsync(entry, cancellationToken);
				return content is null
					? Results.NotFound(new ErrorResponse("FAQ entry not found."))
					: Results.Text(content);
			})
			.WithGroupName("basilapi")
			.WithName("getFaq")
			.WithSummary("Get an FAQ.")
			.WithDescription("""
			                 Returns the entry's text. `{entry}` is the bare name used by `!faq <entry>`.

			                 Returns `404 Not Found` if no entry with this name exists.
			                 """)
			.WithTags("FAQ")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/faqs/{entry}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceFaq")
			.WithSummary("Replace an FAQ.")
			.WithDescription("""
			                 Multipart upload, field name `file`. Replaces the entry's text; the entry's name is fixed by the URL.

			                 Returns `404 Not Found` if no entry with this name exists yet; use `POST /faqs/` to create one.
			                 """ + AdminKeyNote)
			.WithTags("FAQ")
			.WithMultipartFileUpload()
			.Produces<FaqReplacedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new FaqReplacedView("rules", true))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Invalid entry name."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/faqs/{entry}", (string entry, FaqService faq, ILogger<FaqRoutesLog> logger) =>
			{
				if (!faq.DeleteEntry(entry)) return Results.NotFound(new ErrorResponse("FAQ entry not found."));
				logger.LogDebug("FAQ entry deleted via admin API: Entry={Entry}", entry);
				return Results.Json(new FaqDeletedView(entry, true));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteFaq")
			.WithSummary("Delete an FAQ.")
			.WithDescription("""
			                 Deletes the entry and returns a confirmation body.

			                 Returns `404 Not Found` if no entry with this name exists.
			                 """ + AdminKeyNote)
			.WithTags("FAQ")
			.Produces<FaqDeletedView>()
			.WithExample(StatusCodes.Status200OK, new FaqDeletedView("rules", true))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleCreate(HttpContext context, FaqService faq,
		ILogger<FaqRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));
		if (!file.ContentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
			return Results.BadRequest(new ErrorResponse("Only `text/plains*` uploads are accepted."));

		var entry = Path.GetFileNameWithoutExtension(file.FileName);
		await using var stream = file.OpenReadStream();
		var result = await faq.CreateEntryAsync(entry, stream, cancellationToken);
		if (result is FaqService.CreateResult.AlreadyExists or FaqService.CreateResult.InvalidName)
			return result switch
			{
				FaqService.CreateResult.AlreadyExists =>
					Results.Conflict(new ErrorResponse($"'{entry}' already exists.")),
				_ => Results.BadRequest(new ErrorResponse("Invalid entry name."))
			};

		logger.LogDebug("FAQ entry created via admin API: Entry={Entry}", entry);
		return Results.Created($"/faqs/{entry}", new FaqCreatedView(entry, true));
	}

	private static async Task<IResult> HandleReplace(string entry, HttpContext context, FaqService faq,
		ILogger<FaqRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		await using var stream = file.OpenReadStream();
		var result = await faq.ReplaceEntryAsync(entry, stream, cancellationToken);
		if (result is FaqService.ReplaceResult.NotFound or FaqService.ReplaceResult.InvalidName)
			return result == FaqService.ReplaceResult.NotFound
				? Results.NotFound(new ErrorResponse("FAQ entry not found."))
				: Results.BadRequest(new ErrorResponse("Invalid entry name."));

		logger.LogDebug("FAQ entry replaced via admin API: Entry={Entry}", entry);
		return Results.Json(new FaqReplacedView(entry, true));
	}

	/// <summary>Confirmation body for `DELETE /faqs/{entry}`.</summary>
	public sealed record FaqDeletedView(string Entry, bool Deleted);

	/// <summary>Confirmation body for `POST /faqs/`.</summary>
	public sealed record FaqCreatedView(string Entry, bool Created);

	/// <summary>Confirmation body for `PUT /faqs/{entry}`.</summary>
	public sealed record FaqReplacedView(string Entry, bool Replaced);
}