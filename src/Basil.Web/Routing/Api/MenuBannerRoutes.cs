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
///     Reads are public; every write requires administrator authorization. A banner's image is either
///     an uploaded file or an external URL. Both `POST /menu/banners` and `PUT`/`PATCH
///     /menu/banners/{bannerId}` are multipart requests carrying an `image` field that is either the
///     uploaded file or a plain-text external URL — the server tells them apart by how the field was
///     sent, not by its name.
/// </remarks>
internal static class MenuBannerRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	private const string ImageFieldNote = " `image` is either an uploaded file or a plain-text " +
	                                      "external URL, told apart by how the field is sent.";

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
			.WithDescription($"""
			                  Multipart request, fields `image`, `url`, `begins`, `expires`.{ImageFieldNote}

			                  `begins`/`expires` are each optional: a missing/empty `begins` means no lower bound (already current), a missing/empty `expires` means no upper bound (never expires), and omitting both makes the banner permanent — always current.
			                  """ + AdminKeyNote)
			.WithTags("Menu Banners")
			.WithMultipartBody(
				MultipartField.FileOrText("image", true),
				MultipartField.Text("url", true),
				MultipartField.Text("begins", false),
				MultipartField.Text("expires", false))
			.Produces<MenuBannerView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status201Created, SampleView());

		group.MapGet("/menu/banners/{bannerId:int}", async (int bannerId, MenuBannerService banners,
				CancellationToken cancellationToken) =>
			{
				var banner = await banners.FetchByIdAsync(bannerId, cancellationToken);
				return banner is null
					? Results.NotFound(new ErrorResponse("Banner not found."))
					: Results.Json(ToView(banner, banners));
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
			.WithDescription($"""
			                  Multipart request, fields `image`, `url`, `begins`, `expires` — each applied only if present.{ImageFieldNote}

			                  `begins`/`expires` follow the same "empty = no bound" rule as `POST /menu/banners`, but since this route only applies a field when it's present, a missing `begins`/`expires` leaves the stored bound as-is rather than clearing it — to clear an existing bound back to permanent, delete and recreate the banner.

			                  Returns `404 Not Found` if the banner doesn't exist.
			                  """ + AdminKeyNote)
			.WithTags("Menu Banners")
			.WithMultipartBody(
				MultipartField.FileOrText("image", false),
				MultipartField.Text("url", false),
				MultipartField.Text("begins", false),
				MultipartField.Text("expires", false))
			.Produces<MenuBannerView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/menu/banners/{bannerId:int}", async (int bannerId, MenuBannerService banners,
				ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (!await banners.DeleteAsync(bannerId, cancellationToken))
					return Results.NotFound(new ErrorResponse("Banner not found."));
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
	}

	private static async Task<IResult> HandleCreate(HttpContext context, MenuBannerService banners,
		ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart form."));

		var form = await context.Request.ReadFormAsync(cancellationToken);

		var url = form["url"].ToString();
		if (string.IsNullOrEmpty(url)) return Results.BadRequest(new ErrorResponse("Missing 'url' form field."));

		if (!TryParseOptionalDate(form["begins"], "begins", out var begins, out var error) ||
		    !TryParseOptionalDate(form["expires"], "expires", out var expires, out error))
			return Results.BadRequest(new ErrorResponse(error!));

		var file = form.Files.GetFile("image");
		MenuBanner created;
		if (file is not null)
		{
			await using var stream = file.OpenReadStream();
			created = await banners.CreateFromUploadAsync(stream, file.FileName, url, begins, expires,
				cancellationToken);
		}
		else
		{
			var image = form["image"].ToString();
			if (string.IsNullOrEmpty(image))
				return Results.BadRequest(new ErrorResponse("Missing 'image' form field."));
			if (!MenuBannerService.IsExternalUrl(image))
				return Results.BadRequest(
					new ErrorResponse("'image' must be an uploaded file or start with http:// or https://."));
			created = await banners.CreateAsync(image, url, begins, expires, cancellationToken);
		}

		logger.LogInformation("Menu banner created via admin API: Id={Id}", created.Id);
		return Results.Created($"/menu/banners/{created.Id}", ToView(created, banners));
	}

	private static async Task<IResult> HandleUpdate(int bannerId, HttpContext context, MenuBannerService banners,
		ILogger<MenuBannerRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart form."));

		var form = await context.Request.ReadFormAsync(cancellationToken);

		if (!TryParseOptionalDate(form["begins"], "begins", out var begins, out var error) ||
		    !TryParseOptionalDate(form["expires"], "expires", out var expires, out error))
			return Results.BadRequest(new ErrorResponse(error!));

		var urlRaw = form["url"].ToString();
		var url = string.IsNullOrEmpty(urlRaw) ? null : urlRaw;

		var file = form.Files.GetFile("image");
		MenuBanner? updated;
		if (file is not null)
		{
			await using var stream = file.OpenReadStream();
			updated = await banners.ReplaceImageAsync(bannerId, stream, file.FileName, url, begins, expires,
				cancellationToken);
		}
		else
		{
			var imageRaw = form["image"].ToString();
			var image = string.IsNullOrEmpty(imageRaw) ? null : imageRaw;
			if (image is not null && !MenuBannerService.IsExternalUrl(image))
				return Results.BadRequest(
					new ErrorResponse("'image' must be an uploaded file or start with http:// or https://."));
			updated = await banners.UpdateAsync(bannerId, image, url, begins, expires, cancellationToken);
		}

		if (updated is null) return Results.NotFound(new ErrorResponse("Banner not found."));

		logger.LogInformation("Menu banner updated via admin API: Id={Id}", bannerId);
		return Results.Json(ToView(updated, banners));
	}

	/// <summary>
	///     Parses a multipart date field. A missing or empty value means "no bound" and succeeds with
	///     <see langword="null" />.
	/// </summary>
	private static bool TryParseOptionalDate(string? raw, string fieldName, out DateTime? parsed, out string? error)
	{
		parsed = null;
		error = null;

		if (string.IsNullOrEmpty(raw)) return true;

		if (!DateTime.TryParse(raw, out var value))
		{
			error = $"Invalid '{fieldName}' form field.";
			return false;
		}

		parsed = value;
		return true;
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
}