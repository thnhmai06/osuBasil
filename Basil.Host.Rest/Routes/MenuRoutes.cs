using System.Globalization;
using Basil.Application.Content.Messages;
using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Domain.Content;
using Basil.Gateway.Limits;
using Basil.Application.Sessions.Objects;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Http;
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
		faqs.MapGet("/{entry}", GetFaqAsync).Returns<FaqView>().WithSummary("Get a FAQ entry.")
			.WithDescription("An entry that does not exist is answered with 404.");
		faqs.MapPut("/{entry}", SaveFaqAsync).RequireToken().Takes<FaqRequest>().WithSummary("Replace a FAQ entry.")
			.WithDescription("Creates the entry when no entry has the name.");
		faqs.MapDelete("/{entry}", DeleteFaqAsync).RequireToken().WithSummary("Delete a FAQ entry.")
			.WithDescription("Needs the Owner privilege; a caller without it is refused with 403 whether or not the entry exists. A missing entry is answered with 404.");

		var menu = v1.MapGroup("/menu").WithTags("Menu");
		menu.MapGet("/banners", ListBannersAsync).Admitted(WorkClass.Listing)
			.ReturnsList<BannerView>().WithSummary("List main-menu banners.")
			.WithDescription("Each banner says whether it is current.");
		menu.MapPost("/banners", AddBannerAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.Returns<BannerView>(201).WithSummary("Create a main-menu banner.")
			.WithDescription("""
			                 The multipart form carries the picture in `image` and the HTTP or HTTPS address it opens in `url`. `begins` and `expires` are optional ISO 8601 times; a banner without them is always current.

			                 An image over 10 MB is refused with 413 and the error code `tooLarge` when its size is declared, and by closing the connection otherwise; content that is not a PNG, JPEG, GIF or WebP picture closes the connection.
			                 """);
		menu.MapDelete("/banners/{image}", DeleteBannerAsync).RequireToken().WithSummary("Delete a main-menu banner.")
			.WithDescription("The banner is named by the file name of its `image`.");
		menu.MapGet("/seasonals", async (ISender sender, CancellationToken ct) =>
				Api.Ok((await sender.Send(new ListSeasonals(), ct)).Select(r => Path.GetFileName(r.Value)).ToList()))
			.Admitted(WorkClass.Listing)
			.ReturnsList<string>().WithSummary("List seasonal backgrounds.")
			.WithDescription("Each item is the file name of a background.");
		menu.MapPost("/seasonals", AddSeasonalAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.Returns<string>(201).WithSummary("Create a seasonal background.")
			.WithDescription("""
			                 The multipart form carries the picture in `image`.

			                 An image over 10 MB is refused with 413 and the error code `tooLarge` when its size is declared, and by closing the connection otherwise; content that is not a PNG, JPEG, GIF or WebP picture closes the connection.
			                 """);
		menu.MapDelete("/seasonals/{image}", DeleteSeasonalAsync).RequireToken()
			.WithSummary("Delete a seasonal background.")
			.WithDescription("The background is named by its file name.");
		menu.MapGet("/icon", async (ISender sender, CancellationToken ct) =>
		{
			var icon = (await sender.Send(new ReadSettings(), ct)).MenuIcon;
			return Api.Ok(new IconView(icon.Image is { } image ? Path.GetFileName(image.Value) : null,
				icon.Url?.AbsoluteUri));
		}).Returns<IconView>().WithSummary("Get the main-menu icon.")
			.WithDescription("`image` and `url` are `null` when the icon has none.");
		menu.MapPut("/icon", SetIconAsync).Admitted(WorkClass.Upload).DisableAntiforgery().RequireToken()
			.WithSummary("Replace the main-menu icon.")
			.WithDescription("""
			                 The multipart form carries an optional picture in `image` and an optional HTTP or HTTPS address in `url`. Without `image` the current picture stays; without `url` the icon has no link.

			                 An image over 10 MB is refused with 413 and the error code `tooLarge` when its size is declared, and by closing the connection otherwise; content that is not a PNG, JPEG, GIF or WebP picture closes the connection.
			                 """);
		menu.MapDelete("/icon", async (HttpContext context, ISender sender) =>
				(await sender.Send(new ClearMenuIcon(context.Session()), context.RequestAborted)).Answer())
			.RequireToken().WithSummary("Delete the main-menu icon.")
			.WithDescription("Needs the Owner privilege; the icon loses its picture and its link.");
	}

	private static async Task<IResult> GetFaqAsync(string entry, ISender sender, CancellationToken ct)
	{
		return await sender.Send(new ReadFaq(entry), ct) is { } text
			? Api.Ok(new FaqView(entry, text))
			: Api.NotFound("No such entry.");
	}

	private static async Task<IResult> SaveFaqAsync(HttpContext context, string entry, ISender sender)
	{
		if (await sender.Send(new MayChangeFaq(context.Session()), context.RequestAborted) is
		    var may and not ServerActionResult.Ok)
			return may.Refusal();

		var (request, refused) = await context.ReadBodyAsync<FaqRequest>();
		if (refused is not null) return refused;

		if (request!.Text is null) return Api.Invalid("text is required.");
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

	// Judges a menu upload before its body is read: the size it declares, then who asks.
	private static async Task<IResult?> EarlyAsync(HttpContext context, ISender sender, ApiSession session)
	{
		if (!UploadRequest.Accept(context, MaxImageBytes))
			return Api.Error(StatusCodes.Status413PayloadTooLarge, "tooLarge", "The image is too large.");

		var early = await sender.Send(new MayChangeMenu(session), context.RequestAborted);
		return early == ServerActionResult.Ok ? null : early.Refusal();
	}

	private static Task<UploadForm> ReadImageAsync(HttpContext context, params string[] fields)
	{
		return UploadForm.ReadAsync(context, "image", UploadSignature.Image, MaxImageBytes, fields, false,
			context.RequestAborted);
	}

	private static async Task<IResult> AddBannerAsync(HttpContext context, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		var session = context.Session();
		if (await EarlyAsync(context, sender, session) is { } refused) return refused;

		return await UploadRequest.GuardAsync(context, "POST /v1/menu/banners", $"user {session.Record.Id}",
			async () =>
			{
				await using var form = await ReadImageAsync(context, "url", "begins", "expires");
				if (form.File is null) return Api.Invalid("The image field is missing.");
				if (!Uri.TryCreate(form.Field("url"), UriKind.Absolute, out var url))
					return Api.Invalid("url is not valid.");
				if (!TryTime(form.Field("begins"), out var begins) || !TryTime(form.Field("expires"), out var expires))
					return Api.Invalid("begins and expires must be ISO 8601 times.");

				var (result, banner) = await sender.Send(
					new AddMenuBanner(session, form.FileName!, form.File, url, begins, expires),
					context.RequestAborted);
				return result == ServerActionResult.Ok
					? Api.Created(new BannerView(Path.GetFileName(banner!.Image.Value), banner.Url.AbsoluteUri,
						banner.StartsAt, banner.EndsAt, true))
					: result.Refusal();
			});
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

		var session = context.Session();
		if (await EarlyAsync(context, sender, session) is { } refused) return refused;

		return await UploadRequest.GuardAsync(context, "POST /v1/menu/seasonals", $"user {session.Record.Id}",
			async () =>
			{
				await using var form = await ReadImageAsync(context);
				if (form.File is null) return Api.Invalid("The image field is missing.");

				var (result, image) = await sender.Send(new AddMenuSeasonal(session, form.FileName!, form.File),
					context.RequestAborted);
				return result == ServerActionResult.Ok
					? Api.Created(Path.GetFileName(image!.Value.Value))
					: result.Refusal();
			});
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

		var session = context.Session();
		if (await EarlyAsync(context, sender, session) is { } refused) return refused;

		return await UploadRequest.GuardAsync(context, "PUT /v1/menu/icon", $"user {session.Record.Id}",
			async () =>
			{
				await using var form = await ReadImageAsync(context, "url");
				Uri? url = null;
				if (form.Field("url") is { Length: > 0 } text && !Uri.TryCreate(text, UriKind.Absolute, out url))
					return Api.Invalid("url is not valid.");

				return (await sender.Send(new SetMenuIcon(session, form.FileName, form.File, url),
					context.RequestAborted)).Answer();
			});
	}

	public sealed record FaqRequest(string? Text);
}