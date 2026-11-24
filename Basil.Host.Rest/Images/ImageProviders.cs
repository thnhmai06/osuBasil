using Basil.Host.Shared.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Content.Messages;
using Basil.Application.Shared.Storage;
using Basil.Application.Users.Messages;
using Mediator;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Basil.Host.Rest.Images;

/// <summary>Serves the stored pictures of the image hosts; a request it cannot serve goes on to the routes of the host.</summary>
internal sealed partial class ImageServingMiddleware(RequestDelegate next)
{
	private static readonly Lazy<byte[]> DefaultAvatar = new(() =>
	{
		using var stream = typeof(ImageServingMiddleware).Assembly.GetManifestResourceStream("Avatars.default.png")!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	});

	private static readonly DateTimeOffset DefaultAvatarModified = File.GetLastWriteTimeUtc(
		typeof(ImageServingMiddleware).Assembly.Location);

	private static readonly Dictionary<string, BeatmapsetCoverVariant> CoverVariants = new()
	{
		["cover"] = BeatmapsetCoverVariant.Cover, ["card"] = BeatmapsetCoverVariant.Card,
		["list"] = BeatmapsetCoverVariant.List, ["slimcover"] = BeatmapsetCoverVariant.SlimCover
	};

	private static readonly ImageRoute[] Routes =
	[
		new("a", AvatarPath(), async (match, sender, ct) =>
			await sender.Send(new OpenUserAvatar(int.Parse(match.Groups[1].Value)), ct) ??
			new ImageFile(new MemoryStream(DefaultAvatar.Value, false), "image/png", DefaultAvatarModified)),
		new("assets", MenuPath(), (match, sender, ct) =>
		{
			var kind = match.Groups["kind"].Value switch
			{
				"icon" => ContentImageKind.Icon,
				"banners" => ContentImageKind.Banner,
				_ => ContentImageKind.Seasonal
			};
			return sender.Send(new OpenContentImage(kind, match.Groups["name"].Value), ct).AsTask();
		}),
		new("osu", ScreenshotPath(), (match, sender, ct) =>
			sender.Send(new OpenContentImage(ContentImageKind.Screenshot, match.Groups["name"].Value), ct).AsTask()),
		new("assets", CoverPath(), (match, sender, ct) =>
			OpenCover(match, sender, ct, CoverVariants[match.Groups["variant"].Value])),
		new("b", ThumbPath(), (match, sender, ct) =>
			OpenCover(match, sender, ct,
				match.Groups["large"].Success
					? BeatmapsetCoverVariant.ThumbnailLarge
					: BeatmapsetCoverVariant.Thumbnail))
	];

	public async Task InvokeAsync(HttpContext context, ISender sender)
	{
		if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
		{
			var host = context.Request.Host.Host;
			var path = context.Request.Path.Value ?? "";
			foreach (var route in Routes)
			{
				if (!host.StartsWith(route.Label + ".", StringComparison.OrdinalIgnoreCase)) continue;
				var match = route.Path.Match(path);
				if (!match.Success) continue;

				var image = await route.Open(match, sender, context.RequestAborted);
				if (image is null) break;

				await using (image)
				{
					await Serve(context, image, route.Label);
				}

				return;
			}
		}

		await next(context);
	}

	private static async Task<ImageFile?> OpenCover(Match match, ISender sender, CancellationToken ct,
		BeatmapsetCoverVariant variant)
	{
		return int.TryParse(match.Groups["id"].ValueSpan, out var id)
			? await sender.Send(new OpenBeatmapsetCover(id, variant), ct)
			: null;
	}

	private static Task Serve(HttpContext context, ImageFile image, string label)
	{
		// Avatars change when users upload again, so they are cached for a shorter time.
		context.Response.Headers.CacheControl = label == "a" ? "public,max-age=300" : "public,max-age=604800";
		context.Response.Headers.XContentTypeOptions = "nosniff";
		var tag = new EntityTagHeaderValue($"\"{image.LastModified.UtcTicks:x}-{image.Content.Length:x}\"");
		return StoredFile.Send(context, image.Content, image.ContentType, lastModified: image.LastModified,
				entityTag: tag)
			.ExecuteAsync(context);
	}

	[GeneratedRegex(@"^/(\d{1,9})$")]
	private static partial Regex AvatarPath();

	[GeneratedRegex(@"^/menu/(?:(?<kind>banners|seasonals)/(?<name>[^/]+)|(?<kind>icon))$")]
	private static partial Regex MenuPath();

	[GeneratedRegex(@"^/ss/(?<name>[0-9a-fA-F-]+\.[A-Za-z0-9]{1,8})$")]
	private static partial Regex ScreenshotPath();

	[GeneratedRegex(@"^/beatmapsets/(?<id>\d+)/covers/(?<variant>cover|card|list|slimcover)\.jpg$")]
	private static partial Regex CoverPath();

	[GeneratedRegex(@"^/thumb/(?<id>\d+)(?<large>l)?\.jpg$")]
	private static partial Regex ThumbPath();

	private sealed record ImageRoute(
		string Label,
		Regex Path,
		Func<Match, ISender, CancellationToken, Task<ImageFile?>> Open);
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
		var banners = await sender.Send(new ListBanners(true), context.RequestAborted);
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