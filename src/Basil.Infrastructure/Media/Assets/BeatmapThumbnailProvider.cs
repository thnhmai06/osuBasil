using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Serves beatmapset thumbnails on the `b.` host through ImageSharp.Web, resizing the original
///     background on the fly instead of the hand-rolled <c>ImageSharpResizer</c> + manual cache.
/// </summary>
/// <remarks>
///     `/thumb/{setId}.jpg` and `/thumb/{setId}l.jpg` carry no query string — the real osu! client
///     hardcodes these exact paths — so the fixed 80×60/160×120 crop is injected as commands by
///     <c>ConfigureImageSharp</c>'s <c>OnParseCommandsAsync</c> callback (see <see cref="TryGetSize" />),
///     rather than being read from the request. Doesn't match (and so doesn't resolve) when the
///     beatmapset is private, missing, or has no local background — the request falls through to the
///     plain `b.` route, which keeps the existing mirror-fallback behavior.
/// </remarks>
public sealed partial class BeatmapThumbnailProvider : IImageProvider
{
	private readonly BeatmapsetAssetCache _assetCache;
	private readonly IBeatmapsetRepository _beatmapsets;
	private readonly IOptions<StorageOptions> _storage;

	/// <summary>Initializes a new instance of the <see cref="BeatmapThumbnailProvider" /> class.</summary>
	/// <param name="beatmapsets">Resolves the beatmapset a thumbnail request is for.</param>
	/// <param name="storage">The storage folders this provider resolves background files against.</param>
	/// <param name="server">The server's configured domain, used to scope this provider to `b.` hosts.</param>
	/// <param name="assetCache">Resolves a background file for a beatmapset that's only stored as a canonical `.osz`.</param>
	public BeatmapThumbnailProvider(IBeatmapsetRepository beatmapsets, IOptions<StorageOptions> storage,
		IOptions<ServerOptions> server, BeatmapsetAssetCache assetCache)
	{
		_beatmapsets = beatmapsets;
		_storage = storage;
		_assetCache = assetCache;

		var hosts = AssetsHost.BeatmapAssetHostsFor(server.Value.Domain);
		Match = context => AssetsHost.Matches(context, hosts) &&
		                   ThumbPathRegex().IsMatch(context.Request.Path.Value ?? "");
	}

	/// <inheritdoc />
	public ProcessingBehavior ProcessingBehavior => ProcessingBehavior.CommandOnly;

	/// <inheritdoc />
	public Func<HttpContext, bool> Match { get; set; }

	/// <inheritdoc />
	public bool IsValidRequest(HttpContext context)
	{
		return true;
	}

	/// <inheritdoc />
	public async Task<IImageResolver?> GetAsync(HttpContext context)
	{
		var match = ThumbPathRegex().Match(context.Request.Path.Value ?? "");
		if (!match.Success) return null;

		var setId = int.Parse(match.Groups["id"].Value);
		var beatmapset = await _beatmapsets.FetchByIdAsync(setId, context.RequestAborted);
		if (beatmapset is null || beatmapset.IsPrivate) return null;

		var backgroundPath = await BeatmapIngestionService.BackgroundFilePathAsync(_storage.Value, _assetCache,
			beatmapset, context.RequestAborted);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return null;

		return new PhysicalImageResolver(new FileInfo(backgroundPath));
	}

	/// <summary>
	///     Gets the fixed crop size for a `/thumb/...` request path, for
	///     <c>ImageSharpMiddlewareOptions.OnParseCommandsAsync</c> to inject as commands.
	/// </summary>
	/// <param name="path">The request path.</param>
	/// <param name="width">The crop width, when the path matches.</param>
	/// <param name="height">The crop height, when the path matches.</param>
	/// <returns><see langword="true" /> if the path is a thumbnail request; otherwise, <see langword="false" />.</returns>
	public static bool TryGetSize(PathString path, out int width, out int height)
	{
		var match = ThumbPathRegex().Match(path.Value ?? "");
		if (match.Success)
		{
			var large = match.Groups["large"].Success;
			(width, height) = large ? (160, 120) : (80, 60);
			return true;
		}

		(width, height) = (0, 0);
		return false;
	}

	[GeneratedRegex(@"^/thumb/(?<id>\d+)(?<large>l)?\.jpg$")]
	private static partial Regex ThumbPathRegex();
}