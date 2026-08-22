using Basil.Application.Services.Content;
using Basil.Domain.Content;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="MenuBannerRoutes" /> class.</summary>
internal sealed class MenuBannerRoutesLog;

/// <summary>
///     Registers the REST endpoints for managing main-menu banners.
/// </summary>
/// <remarks>
///     Reads are public; every write requires administrator authorization. A banner's image is
///     either an uploaded file or an external URL — `/menu/banners` manages metadata plus the
///     external-URL form, while `/menu/banners/{bannerId}/image` manages the uploaded-file form.
/// </remarks>
internal static class MenuBannerRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/menu/banners` read and admin-key-gated write routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMenuBannerRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/menu/banners", async (MenuBannerService banners, CancellationToken cancellationToken) =>
				Results.Json((await banners.FetchAllAsync(cancellationToken)).Select(b => ToView(b, banners))))
			.WithGroupName("basilapi")
			.WithName("listMenuBanners")
			.WithSummary("List main-menu banners.")
			.WithDescription("Returns every stored banner, current or not.")
			.WithTags("Menu Banners")
			.Produces<IReadOnlyList<MenuBannerView>>()
			.WithExample(StatusCodes.Status200OK, new List<MenuBannerView> { SampleView() });

		group.MapPost("/menu/banners", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createMenuBanner")
			.WithSummary("Create a main-menu banner.")
			.WithDescription("""
			                 Either a multipart upload (fields `file`, `url`, `begins`, `expires`) or a JSON body `{ image, url, begins, expires }` where `image` is an external URL.

			                 `begins`/`expires` are each optional: a missing/null `begins` means no lower bound (already current), a missing/null `expires` means no upper bound (never expires), and omitting both makes the banner permanent — always current.
			                 """ + AdminKeyNote)
			.WithTags("Menu Banners")
			.Produces<MenuBannerView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status201Created, SampleView());

		group.MapGet("/menu/banners/{bannerId:int}", async (int bannerId, MenuBannerService banners,
				CancellationToken cancellationToken) =>
			{
				var banner = await banners.FetchByIdAsync(bannerId, cancellationToken);
				return banner is null ? Results.NotFound() : Results.Json(ToView(banner, banners));
			})
			.WithGroupName("basilapi")
			.WithName("getMenuBanner")
			.WithSummary("Get a main-menu banner.")
			.WithDescription("Returns `404 Not Found` if the banner doesn't exist.")
			.WithTags("Menu Banners")
			.Produces<MenuBannerView>()
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapMethods("/menu/banners/{bannerId:int}", [HttpMethods.Put, HttpMethods.Patch], HandleUpdate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateMenuBanner")
			.WithSummary("Update a main-menu banner.")
			.WithDescription("""
			                 Body: `{ image?, url?, begins?, expires? }`. Each field is applied only if present. Setting `image` here always means an external URL, replacing any uploaded file; use `POST /menu/banners/{bannerId}/image` to upload a file instead.

			                 `begins`/`expires` follow the same "null = no bound" rule as `POST /menu/banners`, but since this route only applies a field when it's present, a missing `begins`/`expires` leaves the stored bound as-is rather than clearing it — to clear an existing bound back to permanent, delete and recreate the banner.

			                 Returns `404 Not Found` if the banner doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Menu Banners")
			.Produces<MenuBannerView>()
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/menu/banners/{bannerId:int}", async (int bannerId, MenuBannerService banners,
				ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (!await banners.DeleteAsync(bannerId, cancellationToken)) return Results.NotFound();
				logger.LogInformation("Menu banner deleted via admin API: Id={Id}", bannerId);
				return Results.Json(new MenuBannerDeletedView(bannerId, true));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteMenuBanner")
			.WithSummary("Delete a main-menu banner.")
			.WithDescription("Deletes the entry and its uploaded file, if any. Returns `404 Not Found` " +
			                 "if the banner doesn't exist." + AdminKeyNote)
			.WithTags("Menu Banners")
			.Produces<MenuBannerDeletedView>()
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/menu/banners/{bannerId:int}/image", HandleUploadImage)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("uploadMenuBannerImage")
			.WithSummary("Upload a main-menu banner image.")
			.WithDescription("Multipart upload, field name `file`. Replaces whatever image is currently " +
			                 "set (uploaded file or external URL). Returns `404 Not Found` if the " +
			                 "banner doesn't exist." + AdminKeyNote)
			.WithTags("Menu Banners")
			.WithMultipartFileUpload()
			.Produces<MenuBannerView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleCreate(HttpContext context, MenuBannerService banners,
		ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken)
	{
		MenuBanner created;
		if (context.Request.HasFormContentType)
		{
			var form = await context.Request.ReadFormAsync(cancellationToken);
			var file = form.Files.GetFile("file");
			if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));
			if (!TryParseWindow(form["url"], form["begins"], form["expires"], out var url, out var begins,
				    out var expires, out var error))
				return Results.BadRequest(new ErrorResponse(error!));

			await using var stream = file.OpenReadStream();
			var extension = Path.GetExtension(file.FileName);
			created = await banners.CreateFromUploadAsync(stream, extension, url!, begins, expires,
				cancellationToken);
		}
		else
		{
			var body = await context.Request.ReadFromJsonAsync<MenuBannerCreateBody>(cancellationToken);
			if (body is null) return Results.BadRequest(new ErrorResponse("Missing or invalid JSON body."));
			if (!MenuBannerService.IsExternalUrl(body.Image))
				return Results.BadRequest(new ErrorResponse("'image' must start with http:// or https://."));

			created = await banners.CreateAsync(body.Image, body.Url, body.Begins, body.Expires, cancellationToken);
		}

		logger.LogInformation("Menu banner created via admin API: Id={Id}", created.Id);
		return Results.Created($"/menu/banners/{created.Id}", ToView(created, banners));
	}

	private static async Task<IResult> HandleUpdate(int bannerId, MenuBannerUpdateBody body, MenuBannerService banners,
		ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (body.Image is not null && !MenuBannerService.IsExternalUrl(body.Image))
			return Results.BadRequest(new ErrorResponse("'image' must start with http:// or https://."));

		var updated = await banners.UpdateAsync(bannerId, body.Image, body.Url, body.Begins, body.Expires,
			cancellationToken);
		if (updated is null) return Results.NotFound();

		logger.LogInformation("Menu banner updated via admin API: Id={Id}", bannerId);
		return Results.Json(ToView(updated, banners));
	}

	private static async Task<IResult> HandleUploadImage(int bannerId, HttpContext context, MenuBannerService banners,
		ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		await using var stream = file.OpenReadStream();
		var extension = Path.GetExtension(file.FileName);
		var updated = await banners.ReplaceImageAsync(bannerId, stream, extension, cancellationToken);
		if (updated is null) return Results.NotFound();

		logger.LogInformation("Menu banner image uploaded via admin API: Id={Id}", bannerId);
		return Results.Json(ToView(updated, banners));
	}

	/// <summary>
	///     Parses the multipart form's <c>url</c>/<c>begins</c>/<c>expires</c> fields.
	///     <c>begins</c>/<c>expires</c> are each optional — a missing or empty value means no bound.
	/// </summary>
	private static bool TryParseWindow(string? url, string? begins, string? expires, out string? outUrl,
		out DateTime? outBegins, out DateTime? outExpires, out string? error)
	{
		outUrl = url;
		outBegins = null;
		outExpires = null;
		error = null;

		if (string.IsNullOrEmpty(url))
		{
			error = "Missing 'url' form field.";
		}
		else if (!string.IsNullOrEmpty(begins))
		{
			if (!DateTime.TryParse(begins, out var parsedBegins)) error = "Invalid 'begins' form field.";
			else outBegins = parsedBegins;
		}

		if (error is null && !string.IsNullOrEmpty(expires))
		{
			if (!DateTime.TryParse(expires, out var parsedExpires)) error = "Invalid 'expires' form field.";
			else outExpires = parsedExpires;
		}

		return error is null;
	}

	private static MenuBannerView ToView(MenuBanner banner, MenuBannerService banners)
	{
		return new MenuBannerView(banner.Id, banners.ResolveImageUrl(banner.Image), banner.Url, banner.Begins,
			banner.Expires, banner.IsCurrent(DateTime.UtcNow));
	}

	private static MenuBannerView SampleView()
	{
		var begins = DateTime.Parse("2026-06-01T00:00:00Z");
		return new MenuBannerView(1, "https://assets.example.com/menu/banners/summer.png",
			"https://example.com/summer-event", begins, begins.AddDays(30), true);
	}

	/// <summary>Response body for the `/menu/banners` read routes.</summary>
	public sealed record MenuBannerView(
		int Id,
		string Image,
		string Url,
		DateTime? Begins,
		DateTime? Expires,
		bool IsCurrent);

	/// <summary>Confirmation body for `DELETE /menu/banners/{bannerId}`.</summary>
	public sealed record MenuBannerDeletedView(int Id, bool Deleted);

	/// <summary>Request body for `POST /menu/banners` (JSON, external-URL form).</summary>
	public sealed record MenuBannerCreateBody(string Image, string Url, DateTime? Begins, DateTime? Expires);

	/// <summary>Request body for `PUT`/`PATCH /menu/banners/{bannerId}`.</summary>
	public sealed record MenuBannerUpdateBody(
		string? Image = null,
		string? Url = null,
		DateTime? Begins = null,
		DateTime? Expires = null);
}