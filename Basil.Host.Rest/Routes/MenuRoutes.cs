using System.Globalization;
using Basil.Application.Content.Messages;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Domain.Content;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class MenuRoutes
{
	private const long MaxImageBytes = 10L * 1024 * 1024;

	public static void MapMenu(this RouteGroupBuilder v1)
	{
		var faqs = v1.MapGroup("/faqs").WithTags("Faqs");
		faqs.MapGet("/",
				async (ISender sender, CancellationToken ct) => Api.Ok(await sender.Send(new ListFaqs(), ct)))
			.Admitted(WorkClass.Listing)
			.WithSummary("List FAQ entries.")
			.WithDescription("An entry in a folder has the folder in its name, separated by `:`.");
		faqs.MapGet("/{entry}", GetFaqAsync).WithSummary("Get a FAQ entry.");
		faqs.MapPut("/{entry}", SaveFaqAsync).RequireToken().WithSummary("Replace a FAQ entry.")
			.WithDescription("Creates the entry when no entry has the name.");
		faqs.MapDelete("/{entry}", DeleteFaqAsync).RequireToken().WithSummary("Delete a FAQ entry.");

		var menu = v1.MapGroup("/menu").WithTags("Menu");
		menu.MapGet("/banners", ListBannersAsync).Admitted(WorkClass.Listing)
			.WithSummary("List main-menu banners.")
			.WithDescription("Each banner says whether it is current.");
		menu.MapPost("/banners", AddBannerAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.WithSummary("Create a main-menu banner.")
			.WithDescription("""
			                 The multipart form carries the picture in `image` and the HTTP or HTTPS address it opens in `url`. `begins` and `expires` are optional ISO 8601 times; a banner without them is always current.

			                 An image over 10 MB is refused with 422 and the error code `tooLarge`.
			                 """);
		menu.MapDelete("/banners/{image}", DeleteBannerAsync).RequireToken().WithSummary("Delete a main-menu banner.")
			.WithDescription("The banner is named by the file name of its `image`.");
		menu.MapGet("/seasonals", async (ISender sender, CancellationToken ct) =>
				Api.Ok((await sender.Send(new ListSeasonals(), ct)).Select(r => Path.GetFileName(r.Value)).ToList()))
			.Admitted(WorkClass.Listing)
			.WithSummary("List seasonal backgrounds.");
		menu.MapPost("/seasonals", AddSeasonalAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.WithSummary("Create a seasonal background.")
			.WithDescription("""
			                 The multipart form carries the picture in `image`.

			                 An image over 10 MB is refused with 422 and the error code `tooLarge`.
			                 """);
		menu.MapDelete("/seasonals/{image}", DeleteSeasonalAsync).RequireToken()
			.WithSummary("Delete a seasonal background.")
			.WithDescription("The background is named by its file name.");
		menu.MapGet("/icon", async (ISender sender, CancellationToken ct) =>
		{
			var icon = (await sender.Send(new ReadSettings(), ct)).MenuIcon;
			return Api.Ok(new IconView(icon.Image is { } image ? Path.GetFileName(image.Value) : null,
				icon.Url?.AbsoluteUri));
		}).WithSummary("Get the main-menu icon.");
		menu.MapPut("/icon", SetIconAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.WithSummary("Replace the main-menu icon.")
			.WithDescription("""
			                 The multipart form carries an optional picture in `image` and an optional HTTP or HTTPS address in `url`. Without `image` the current picture stays; without `url` the icon has no link.

			                 An image over 10 MB is refused with 422 and the error code `tooLarge`.
			                 """);
		menu.MapDelete("/icon", async (HttpContext context, ISender sender) =>
				(await sender.Send(new ClearMenuIcon(context.Session()), context.RequestAborted)).Answer())
			.RequireToken().WithSummary("Delete the main-menu icon.");
	}

	private static async Task<IResult> GetFaqAsync(string entry, ISender sender, CancellationToken ct)
	{
		return await sender.Send(new ReadFaq(entry), ct) is { } text
			? Api.Ok(new FaqView(entry, text))
			: Api.NotFound("No such entry.");
	}

	private static async Task<IResult> SaveFaqAsync(HttpContext context, string entry, FaqRequest request,
		ISender sender)
	{
		if (request.Text is null) return Api.Invalid("text is required.");
		return (await sender.Send(new SaveFaq(context.Session(), entry, request.Text), context.RequestAborted))
			.Answer();
	}

	private static async Task<IResult> DeleteFaqAsync(HttpContext context, string entry, ISender sender)
	{
		return (await sender.Send(new DeleteFaq(context.Session(), entry), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> ListBannersAsync(ISender sender, TimeProvider clock,
		CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		return Api.Ok((await sender.Send(new ListBanners(), ct)).Select(b =>
			BannerView.Of(b, now)).ToList());
	}

	private static async Task<(IFormFile? File, IResult? Error)> ImageAsync(HttpContext context,
		IFormCollection? form = null)
	{
		form ??= await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("image") is not { } file) return (null, Api.Invalid("The image field is missing."));

		return file.Length > MaxImageBytes ? (null, Api.Invalid("The image is too large.", "tooLarge")) : (file, null);
	}

	private static async Task<IResult> AddBannerAsync(HttpContext context, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		await UploadLimit.AllowAsync(context, sender);

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		var (file, error) = await ImageAsync(context, form);
		if (error is not null) return error;
		if (!Uri.TryCreate(form["url"].ToString(), UriKind.Absolute, out var url))
			return Api.Invalid("url is not valid.");
		if (!TryTime(form["begins"].ToString(), out var begins) ||
		    !TryTime(form["expires"].ToString(), out var expires))
			return Api.Invalid("begins and expires must be ISO 8601 times.");

		await using var stream = file!.OpenReadStream();
		var (result, banner) = await sender.Send(
			new AddMenuBanner(context.Session(), file.FileName, stream, url, begins, expires),
			context.RequestAborted);
		return result == ServerActionResult.Ok
			? Api.Created(new BannerView(Path.GetFileName(banner!.Image.Value), banner.Url.AbsoluteUri,
				banner.StartsAt, banner.EndsAt, true))
			: result.Refusal();
	}

	private static bool TryTime(string text, out DateTimeOffset? value)
	{
		value = null;
		if (text.Length == 0) return true;

		var ok = DateTimeOffset.TryParse(text, null, DateTimeStyles.RoundtripKind, out var parsed);
		value = ok ? parsed : null;
		return ok;
	}

	private static async Task<IResult> DeleteBannerAsync(HttpContext context, string image, ISender sender)
	{
		var banner =
			(await sender.Send(new ListBanners(), context.RequestAborted)).FirstOrDefault(b =>
				Path.GetFileName(b.Image.Value) == image);
		return banner is null
			? Api.NotFound("No such banner.")
			: (await sender.Send(new DeleteMenuBanner(context.Session(), banner), context.RequestAborted))
			.Answer();
	}

	private static async Task<IResult> AddSeasonalAsync(HttpContext context, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		var (file, error) = await ImageAsync(context);
		if (error is not null) return error;

		await using var stream = file!.OpenReadStream();
		var (result, image) = await sender.Send(new AddMenuSeasonal(context.Session(), file.FileName, stream),
			context.RequestAborted);
		return result == ServerActionResult.Ok ? Api.Created(Path.GetFileName(image!.Value.Value)) : result.Refusal();
	}

	private static async Task<IResult> DeleteSeasonalAsync(HttpContext context, string image, ISender sender)
	{
		var reference = (await sender.Send(new ListSeasonals(), context.RequestAborted)).Cast<ContentReference?>()
			.FirstOrDefault(r => Path.GetFileName(r!.Value.Value) == image);
		return reference is null
			? Api.NotFound("No such background.")
			: (await sender.Send(new DeleteMenuSeasonal(context.Session(), reference.Value), context.RequestAborted))
			.Answer();
	}

	private static async Task<IResult> SetIconAsync(HttpContext context, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		await UploadLimit.AllowAsync(context, sender);

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		Uri? url = null;
		if (form["url"].ToString() is { Length: > 0 } text && !Uri.TryCreate(text, UriKind.Absolute, out url))
			return Api.Invalid("url is not valid.");
		var file = form.Files.GetFile("image");
		if (file?.Length > MaxImageBytes) return Api.Invalid("The image is too large.", "tooLarge");

		await using var stream = file?.OpenReadStream();
		return (await sender.Send(new SetMenuIcon(context.Session(), file?.FileName, stream, url),
			context.RequestAborted)).Answer();
	}

	public sealed record FaqRequest(string? Text);
}