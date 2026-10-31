using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;

namespace Basil.Host.Rest.Routes;

/// <summary>FAQ entries and the main menu: public to read, for content managers to change.</summary>
internal static class MenuRoutes
{
	private const long MaxImageBytes = 10L * 1024 * 1024;

	/// <summary>The text of a FAQ entry.</summary>
	public sealed record FaqRequest(string? Text);

	public static void MapMenu(this RouteGroupBuilder v1)
	{
		var faqs = v1.MapGroup("/faqs").WithTags("Faqs");
		faqs.MapGet("/", async (IFaqStorage storage, CancellationToken ct) => Api.Ok((await storage.ListAsync(ct)).ToList()))
			.WithSummary("List the FAQ entries.").WithDescription("A folder inside the entry names is written with `:`.");
		faqs.MapGet("/{entry}", GetFaqAsync).WithSummary("Retrieve a FAQ entry.");
		faqs.MapPut("/{entry}", SaveFaqAsync).RequireToken().WithSummary("Create or replace a FAQ entry.");
		faqs.MapDelete("/{entry}", DeleteFaqAsync).RequireToken().WithSummary("Delete a FAQ entry.");

		var menu = v1.MapGroup("/menu").WithTags("Menu");
		menu.MapGet("/banners", ListBannersAsync).WithSummary("List the main-menu banners.");
		menu.MapPost("/banners", AddBannerAsync).DisableAntiforgery().RequireToken()
			.WithSummary("Add a main-menu banner from an image in the `image` field and the address in `url`.")
			.WithDescription("`begins` and `expires` are optional ISO 8601 times; a banner without them is always current.");
		menu.MapDelete("/banners/{image}", DeleteBannerAsync).RequireToken().WithSummary("Delete a main-menu banner.");
		menu.MapGet("/seasonals", async (IMenuSeasonalsStorage storage, CancellationToken ct) =>
				Api.Ok((await storage.ListAsync(ct)).Select(r => Path.GetFileName(r.Value)).ToList()))
			.WithSummary("List the seasonal backgrounds.");
		menu.MapPost("/seasonals", AddSeasonalAsync).DisableAntiforgery().RequireToken()
			.WithSummary("Add a seasonal background from an image in the `image` field.");
		menu.MapDelete("/seasonals/{image}", DeleteSeasonalAsync).RequireToken().WithSummary("Delete a seasonal background.");
		menu.MapGet("/icon", async (ISettingsRepository settings, CancellationToken ct) =>
			{
				var icon = (await settings.GetAsync(ct)).MenuIcon;
				return Api.Ok(new IconView(icon.Image is { } image ? Path.GetFileName(image.Value) : null, icon.Url?.AbsoluteUri));
			}).WithSummary("Retrieve the main-menu icon.");
		menu.MapPut("/icon", SetIconAsync).DisableAntiforgery().RequireToken()
			.WithSummary("Set the main-menu icon from an optional image in the `image` field and an optional address in `url`.");
		menu.MapDelete("/icon", async (HttpContext context, Server server) =>
				(await server.ScopeTo(context.Connection()).Content.ClearIconAsync(context.RequestAborted)).Answer())
			.RequireToken().WithSummary("Remove the main-menu icon.");
	}

	private static async Task<IResult> GetFaqAsync(string entry, IFaqStorage storage, CancellationToken ct)
	{
		try
		{
			using var reader = await storage.ReadAsync(entry, ct);
			return reader is null ? Api.NotFound("No such entry.") : Api.Ok(new FaqView(entry, await reader.ReadToEndAsync(ct)));
		}
		catch (ArgumentException)
		{
			return Api.NotFound("No such entry.");
		}
	}

	private static async Task<IResult> SaveFaqAsync(HttpContext context, string entry, FaqRequest request, Server server)
	{
		if (request.Text is null) return Api.Invalid("text is required.");

		try
		{
			return (await server.ScopeTo(context.Connection()).Content.SaveFaqAsync(entry, request.Text, context.RequestAborted)).Answer();
		}
		catch (ArgumentException)
		{
			return Api.Invalid("The entry name is not valid.");
		}
	}

	private static async Task<IResult> DeleteFaqAsync(HttpContext context, string entry, Server server)
	{
		try
		{
			return (await server.ScopeTo(context.Connection()).Content.DeleteFaqAsync(entry, context.RequestAborted)).Answer();
		}
		catch (ArgumentException)
		{
			return Api.NotFound("No such entry.");
		}
	}

	private static async Task<IResult> ListBannersAsync(IMenuBannerRepository banners, TimeProvider clock, CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		return Api.Ok((await banners.ListAsync(ct)).Select(b =>
			BannerView.Of(b, now)).ToList());
	}

	private static async Task<(IFormFile? File, IResult? Error)> ImageAsync(HttpContext context, IFormCollection? form = null)
	{
		form ??= await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("image") is not { } file) return (null, Api.Invalid("The image field is missing."));

		return file.Length > MaxImageBytes ? (null, Api.Invalid("The image is too large.", "tooLarge")) : (file, null);
	}

	private static async Task<IResult> AddBannerAsync(HttpContext context, Server server)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		var (file, error) = await ImageAsync(context, form);
		if (error is not null) return error;
		if (!Uri.TryCreate(form["url"].ToString(), UriKind.Absolute, out var url)) return Api.Invalid("url is not valid.");
		if (!TryTime(form["begins"].ToString(), out var begins) || !TryTime(form["expires"].ToString(), out var expires))
			return Api.Invalid("begins and expires must be ISO 8601 times.");

		try
		{
			await using var stream = file!.OpenReadStream();
			var (result, banner) = await server.ScopeTo(context.Connection()).Content.AddBannerAsync(file.FileName, stream, url,
				begins, expires, context.RequestAborted);
			return result.IsOk
				? Api.Created(new BannerView(Path.GetFileName(banner!.Image.Value), banner.Url.AbsoluteUri, banner.StartsAt, banner.EndsAt, true))
				: result.Refusal();
		}
		catch (Exception ex) when (ex is ArgumentException)
		{
			return Api.Invalid("The banner is not valid.");
		}
	}

	private static bool TryTime(string text, out DateTimeOffset? value)
	{
		value = null;
		if (text.Length == 0) return true;

		var ok = DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed);
		value = ok ? parsed : null;
		return ok;
	}

	private static async Task<IResult> DeleteBannerAsync(HttpContext context, string image, IMenuBannerRepository banners, Server server)
	{
		var banner = (await banners.ListAsync(context.RequestAborted)).FirstOrDefault(b => Path.GetFileName(b.Image.Value) == image);
		return banner is null
			? Api.NotFound("No such banner.")
			: (await server.ScopeTo(context.Connection()).Content.DeleteBannerAsync(banner, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> AddSeasonalAsync(HttpContext context, Server server)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		var (file, error) = await ImageAsync(context);
		if (error is not null) return error;

		try
		{
			await using var stream = file!.OpenReadStream();
			var (result, image) = await server.ScopeTo(context.Connection()).Content.AddSeasonalAsync(file.FileName, stream, context.RequestAborted);
			return result.IsOk ? Api.Created(Path.GetFileName(image!.Value.Value)) : result.Refusal();
		}
		catch (ArgumentException)
		{
			return Api.Invalid("The file name is not valid.");
		}
	}

	private static async Task<IResult> DeleteSeasonalAsync(HttpContext context, string image, IMenuSeasonalsStorage seasonals, Server server)
	{
		var reference = (await seasonals.ListAsync(context.RequestAborted)).Cast<ContentReference?>()
			.FirstOrDefault(r => Path.GetFileName(r!.Value.Value) == image);
		return reference is null
			? Api.NotFound("No such background.")
			: (await server.ScopeTo(context.Connection()).Content.DeleteSeasonalAsync(reference.Value, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> SetIconAsync(HttpContext context, Server server)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form is required.");

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		Uri? url = null;
		if (form["url"].ToString() is { Length: > 0 } text && !Uri.TryCreate(text, UriKind.Absolute, out url))
			return Api.Invalid("url is not valid.");
		var file = form.Files.GetFile("image");
		if (file?.Length > MaxImageBytes) return Api.Invalid("The image is too large.", "tooLarge");

		try
		{
			await using var stream = file?.OpenReadStream();
			return (await server.ScopeTo(context.Connection()).Content.SetIconAsync(file?.FileName, stream, url, context.RequestAborted)).Answer();
		}
		catch (ArgumentException)
		{
			return Api.Invalid("The icon is not valid.");
		}
	}
}
