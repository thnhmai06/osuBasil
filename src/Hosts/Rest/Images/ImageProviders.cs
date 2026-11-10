using System.Text.RegularExpressions;
using Basil.Application.Beatmaps.Messages;
using Basil.Application.Content.Storage;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Host.Rest.Images;

/// <summary>Avatars on <c>a.&lt;domain&gt;/{userId}</c>; a user without one shows the default avatar.</summary>
public sealed partial class AvatarProvider(IUserRepository users, IUserAvatarStorage avatars)
	: StreamImageProvider("a", path => AvatarPath().IsMatch(path.Value ?? ""))
{
	/// <inheritdoc />
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var id = int.Parse(AvatarPath().Match(path.Value!).Groups[1].Value);
		if (await users.GetAsync(id, ct) is { } user &&
		    await avatars.OpenAsync(user, ct) is { } own)
			return own;

		return typeof(AvatarProvider).Assembly.GetManifestResourceStream("Avatars.default.png");
	}

	[GeneratedRegex(@"^/(\d+)$")]
	private static partial Regex AvatarPath();
}

/// <summary>Menu banners, seasonal backgrounds and the icon on <c>assets.&lt;domain&gt;/menu/...</c>.</summary>
public sealed partial class MenuImageProvider(
	IMenuBannerStorage banners,
	IMenuSeasonalsStorage seasonals,
	IMenuIconStorage icon) : StreamImageProvider("assets", path => MenuPath().IsMatch(path.Value ?? ""))
{
	/// <inheritdoc />
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var match = MenuPath().Match(path.Value!);
		var name = match.Groups["name"].Value;
		return match.Groups["kind"].Value switch
		{
			"icon" => (await icon.OpenAsync(ct))?.Content,
			"banners" => await OpenNamedAsync(await banners.ListAsync(ct), name, banners.OpenAsync,
				ct),
			_ => await OpenNamedAsync(await seasonals.ListAsync(ct), name, seasonals.OpenAsync,
				ct)
		};
	}

	internal static async Task<Stream?> OpenNamedAsync(IEnumerable<ContentReference> list, string name,
		Func<ContentReference, CancellationToken, Task<Stream?>> open, CancellationToken ct)
	{
		foreach (var reference in list)
			if (Path.GetFileName(reference.Value).Equals(name, StringComparison.OrdinalIgnoreCase))
				return await open(reference, ct);

		return null;
	}

	[GeneratedRegex(@"^/menu/(?:(?<kind>banners|seasonals)/(?<name>[^/]+)|(?<kind>icon))$")]
	private static partial Regex MenuPath();
}

/// <summary>Screenshots on <c>osu.&lt;domain&gt;/ss/{id}.{ext}</c>, which the game opens in a browser after an upload.</summary>
public sealed partial class ScreenshotProvider(IScreenshotStorage screenshots)
	: StreamImageProvider("osu", path => ScreenshotPath().IsMatch(path.Value ?? ""))
{
	/// <inheritdoc />
	protected override Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var name = ScreenshotPath().Match(path.Value!).Groups["name"].Value;
		return OpenByName(name, ct);
	}

	private async Task<Stream?> OpenByName(string name, CancellationToken ct)
	{
		return await MenuImageProvider.OpenNamedAsync(await screenshots.ListAsync(ct), name,
			screenshots.OpenAsync, ct);
	}

	[GeneratedRegex(@"^/ss/(?<name>[0-9a-fA-F-]+\.[A-Za-z0-9]{1,8})$")]
	private static partial Regex ScreenshotPath();
}

/// <summary>Beatmapset thumbnails and covers; a hidden set shows nothing, because every image is public.</summary>
public sealed partial class BeatmapImageProvider(ISender sender) : StreamImageProvider("assets", path => CoverPath().IsMatch(path.Value ?? ""))
{
	private static readonly Dictionary<string, (int Width, int Height)> Variants = new()
	{
		["cover"] = (900, 250), ["card"] = (400, 140), ["list"] = (150, 150), ["slimcover"] = (1920, 360)
	};

	/// <summary>Finds the crop size a cover path asks for.</summary>
	/// <param name="path">The request path.</param>
	/// <param name="width">The width of the crop.</param>
	/// <param name="height">The height of the crop.</param>
	/// <returns><see langword="true" /> when the path is a cover path.</returns>
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

	/// <inheritdoc />
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var match = CoverPath().Match(path.Value!);
		if (!Variants.ContainsKey(match.Groups["variant"].Value)) return null;
		return await sender.Send(new OpenBeatmapsetCover(int.Parse(match.Groups["id"].Value)), ct);
	}

	[GeneratedRegex(@"^/beatmapsets/(?<id>\d+)/covers/(?<variant>[a-z]+)\.jpg$")]
	private static partial Regex CoverPath();
}

/// <summary>Beatmapset thumbnails on <c>b.&lt;domain&gt;/thumb/{id}.jpg</c>, the fixed paths of the game client.</summary>
public sealed partial class BeatmapThumbnailProvider(ISender sender) : StreamImageProvider("b", path => ThumbPath().IsMatch(path.Value ?? ""))
{
	/// <summary>Finds the crop size a thumbnail path asks for.</summary>
	/// <param name="path">The request path.</param>
	/// <param name="width">The width of the crop.</param>
	/// <param name="height">The height of the crop.</param>
	/// <returns><see langword="true" /> when the path is a thumbnail path.</returns>
	public static bool TryGetSize(PathString path, out int width, out int height)
	{
		var match = ThumbPath().Match(path.Value ?? "");
		(width, height) = !match.Success ? (0, 0) : match.Groups["large"].Success ? (160, 120) : (80, 60);
		return match.Success;
	}

	/// <inheritdoc />
	protected override async Task<Stream?> OpenAsync(PathString path, CancellationToken ct)
	{
		var id = int.Parse(ThumbPath().Match(path.Value!).Groups["id"].Value);
		return await sender.Send(new OpenBeatmapsetCover(id), ct);
	}

	[GeneratedRegex(@"^/thumb/(?<id>\d+)(?<large>l)?\.jpg$")]
	private static partial Regex ThumbPath();
}