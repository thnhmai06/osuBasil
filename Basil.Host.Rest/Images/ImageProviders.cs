using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Content.Messages;
using Basil.Application.Users.Messages;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Host.Rest.Images;

public sealed partial class AvatarProvider(ISender sender)
	: StreamImageProvider("a", path => AvatarPath().IsMatch(path.Value ?? ""))
{
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var id = int.Parse(AvatarPath().Match(path.Value!).Groups[1].Value);
		return await sender.Send(new OpenUserAvatar(id), ct) ??
		       typeof(AvatarProvider).Assembly.GetManifestResourceStream("Avatars.default.png");
	}

	[GeneratedRegex(@"^/(\d+)$")]
	private static partial Regex AvatarPath();
}

public sealed partial class MenuImageProvider(ISender sender)
	: StreamImageProvider("assets", path => MenuPath().IsMatch(path.Value ?? ""))
{
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var match = MenuPath().Match(path.Value!);
		var name = match.Groups["name"].Value;
		var kind = match.Groups["kind"].Value switch
		{
			"icon" => ContentImageKind.Icon,
			"banners" => ContentImageKind.Banner,
			_ => ContentImageKind.Seasonal
		};
		return await sender.Send(new OpenContentImage(kind, name), ct);
	}

	[GeneratedRegex(@"^/menu/(?:(?<kind>banners|seasonals)/(?<name>[^/]+)|(?<kind>icon))$")]
	private static partial Regex MenuPath();
}

public sealed partial class ScreenshotProvider(ISender sender)
	: StreamImageProvider("osu", path => ScreenshotPath().IsMatch(path.Value ?? ""))
{
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var name = ScreenshotPath().Match(path.Value!).Groups["name"].Value;
		return await sender.Send(new OpenContentImage(ContentImageKind.Screenshot, name), ct);
	}

	[GeneratedRegex(@"^/ss/(?<name>[0-9a-fA-F-]+\.[A-Za-z0-9]{1,8})$")]
	private static partial Regex ScreenshotPath();
}

public sealed partial class BeatmapImageProvider(ISender sender)
	: StreamImageProvider("assets", path => CoverPath().IsMatch(path.Value ?? ""))
{
	private static readonly Dictionary<string, (int Width, int Height)> Variants = new()
	{
		["cover"] = (900, 250), ["card"] = (400, 140), ["list"] = (150, 150), ["slimcover"] = (1920, 360)
	};

	public static bool TryGetSize(PathString path, out int width, out int height)
	{
		var match = CoverPath().Match(path.Value ?? "");
		if (match.Success && Variants.TryGetValue(match.Groups["variant"].Value, out var size))
		{
			(width, height) = size;
			return true;
		}

		(width, height) = (0, 0);
		return false;
	}

	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var match = CoverPath().Match(path.Value!);
		if (!Variants.ContainsKey(match.Groups["variant"].Value)) return null;
		return await sender.Send(new OpenBeatmapsetCover(int.Parse(match.Groups["id"].Value)), ct);
	}

	[GeneratedRegex(@"^/beatmapsets/(?<id>\d+)/covers/(?<variant>[a-z]+)\.jpg$")]
	private static partial Regex CoverPath();
}

public sealed partial class BeatmapThumbnailProvider(ISender sender)
	: StreamImageProvider("b", path => ThumbPath().IsMatch(path.Value ?? ""))
{
	public static bool TryGetSize(PathString path, out int width, out int height)
	{
		var match = ThumbPath().Match(path.Value ?? "");
		(width, height) = !match.Success ? (0, 0) : match.Groups["large"].Success ? (160, 120) : (80, 60);
		return match.Success;
	}

	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var id = int.Parse(ThumbPath().Match(path.Value!).Groups["id"].Value);
		return await sender.Send(new OpenBeatmapsetCover(id), ct);
	}

	[GeneratedRegex(@"^/thumb/(?<id>\d+)(?<large>l)?\.jpg$")]
	private static partial Regex ThumbPath();
}

internal sealed partial class BeatmapPreviewMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context, ISender sender)
	{
		var match = PreviewPath().Match(context.Request.Path.Value ?? "");
		if (!match.Success || !context.Request.Host.Host.StartsWith("b.", StringComparison.OrdinalIgnoreCase))
		{
			await next(context);
			return;
		}

		var file = int.TryParse(match.Groups["id"].ValueSpan, out var id)
			? await sender.Send(new OpenBeatmapsetFile(null, id, BeatmapsetFileType.Preview), context.RequestAborted)
			: null;
		if (file is null)
		{
			context.Response.StatusCode = StatusCodes.Status404NotFound;
			return;
		}

		await using (file.Content)
		{
			context.Response.ContentType = "audio/mpeg";
			context.Response.Headers.XContentTypeOptions = "nosniff";
			context.Response.Headers.CacheControl = "public,max-age=300";
			await file.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
		}
	}

	[GeneratedRegex(@"^/preview/(?<id>\d+)\.mp3$")]
	private static partial Regex PreviewPath();
}

internal sealed class MenuContentMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context, ISender sender, TimeProvider clock,
		IOptions<RestOptions> options)
	{
		var host = context.Request.Host.Host;
		if (!HttpMethods.IsGet(context.Request.Method) ||
		    context.Request.Path != "/menu-content.json" ||
		    !host.StartsWith("assets.", StringComparison.OrdinalIgnoreCase))
		{
			await next(context);
			return;
		}

		var now = clock.GetUtcNow();
		var banners = await sender.Send(new ListBanners(), context.RequestAborted);
		var images = banners.Select(b => new MenuContentImage(
			b.Image.IsOffline
				? $"https://assets.{options.Value.Domain}/menu/banners/{Path.GetFileName(b.Image.Value)}"
				: b.Image.Value,
			b.Url.AbsoluteUri, b.IsCurrent(now), b.StartsAt?.ToString("O"), b.EndsAt?.ToString("O"))).ToList();
		await context.Response.WriteAsJsonAsync(new MenuContentResponse(images), context.RequestAborted);
	}
}

internal sealed record MenuContentResponse([property: JsonPropertyName("images")] IReadOnlyList<MenuContentImage> Images);

internal sealed record MenuContentImage(
	[property: JsonPropertyName("image")] string Image,
	[property: JsonPropertyName("url")] string Url,
	[property: JsonPropertyName("IsCurrent")] bool IsCurrent,
	[property: JsonPropertyName("begins")] string? Begins,
	[property: JsonPropertyName("expires")] string? Expires);