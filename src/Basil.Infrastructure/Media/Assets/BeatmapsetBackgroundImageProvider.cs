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
///     Serves beatmap/beatmapset backgrounds and cover variants on the `assets.` host through
///     ImageSharp.Web.
/// </summary>
/// <remarks>
///     Handles three request shapes: a beatmap's background (unresized), a beatmapset's preview
///     background (unresized, the lowest-id beatmap's), and the `covers/{variant}.jpg` family
///     (resized, cropped to a fixed size per <see cref="TryGetCoverSize" />). Doesn't match (and so
///     doesn't resolve) when the beatmapset is private, missing, or has no local background — the
///     request falls through to the plain `assets.` route, which keeps the existing private-check/
///     local-only-fallback behavior.
/// </remarks>
public sealed partial class BeatmapsetBackgroundImageProvider : IImageProvider
{
	private readonly IBeatmapRepository _beatmaps;
	private readonly IBeatmapsetRepository _beatmapsets;
	private readonly IOptions<StorageOptions> _storage;

	/// <summary>Initializes a new instance of the <see cref="BeatmapsetBackgroundImageProvider" /> class.</summary>
	/// <param name="beatmapsets">Resolves the beatmapset a background/cover request is for.</param>
	/// <param name="beatmaps">Resolves an individual beatmap's background, when the request names one.</param>
	/// <param name="storage">The storage folders this provider resolves background files against.</param>
	/// <param name="server">The server's configured domain, used to scope this provider to `assets.` hosts.</param>
	public BeatmapsetBackgroundImageProvider(IBeatmapsetRepository beatmapsets, IBeatmapRepository beatmaps,
		IOptions<StorageOptions> storage, IOptions<ServerOptions> server)
	{
		_beatmapsets = beatmapsets;
		_beatmaps = beatmaps;
		_storage = storage;

		var hosts = AssetsHost.AssetsHostsFor(server.Value.Domain);
		Match = context => AssetsHost.Matches(context, hosts) && IsBackgroundPath(context.Request.Path);
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
		var path = context.Request.Path.Value ?? "";
		var cancellationToken = context.RequestAborted;

		var beatmapMatch = BeatmapBackgroundRegex().Match(path);
		if (beatmapMatch.Success)
		{
			var mapsetId = int.Parse(beatmapMatch.Groups["mapsetId"].Value);
			var beatmapId = int.Parse(beatmapMatch.Groups["beatmapId"].Value);
			var bmap = await _beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, cancellationToken: cancellationToken);
			if (bmap is null || bmap.Beatmapset.Id != mapsetId) return null;

			return await ResolveAsync(BeatmapIngestionService.BackgroundFilePath(_storage.Value, bmap));
		}

		var mapsetMatch = MapsetBackgroundRegex().Match(path);
		var coverMatch = CoverRegex().Match(path);
		var id = mapsetMatch.Success ? mapsetMatch.Groups["mapsetId"].Value : coverMatch.Groups["mapsetId"].Value;
		if (!mapsetMatch.Success && !coverMatch.Success) return null;

		var mapset = await _beatmapsets.FetchByIdAsync(int.Parse(id), cancellationToken);
		if (mapset is null || mapset.IsPrivate) return null;

		return await ResolveAsync(BeatmapIngestionService.BackgroundFilePath(_storage.Value, mapset));
	}

	private static Task<IImageResolver?> ResolveAsync(string? backgroundPath)
	{
		IImageResolver? resolver = backgroundPath is not null && File.Exists(backgroundPath)
			? new PhysicalFileImageResolver(new FileInfo(backgroundPath))
			: null;
		return Task.FromResult(resolver);
	}

	private static bool IsBackgroundPath(PathString path)
	{
		var value = path.Value ?? "";
		return BeatmapBackgroundRegex().IsMatch(value) || MapsetBackgroundRegex().IsMatch(value) ||
		       CoverRegex().IsMatch(value);
	}

	/// <summary>
	///     Gets the fixed crop size for a `covers/{variant}.jpg` request path, for
	///     <c>ImageSharpMiddlewareOptions.OnParseCommandsAsync</c> to inject as commands.
	/// </summary>
	/// <remarks>
	///     Sizes here are a best-effort approximation (no verified primary-source dimensions were
	///     available at implementation time) — adjust if real osu!-web values are confirmed later.
	/// </remarks>
	/// <param name="path">The request path.</param>
	/// <param name="width">The crop width, when the path matches.</param>
	/// <param name="height">The crop height, when the path matches.</param>
	/// <returns><see langword="true" /> if the path is a cover request; otherwise, <see langword="false" />.</returns>
	public static bool TryGetCoverSize(PathString path, out int width, out int height)
	{
		var match = CoverRegex().Match(path.Value ?? "");
		if (match.Success)
		{
			(width, height) = match.Groups["variant"].Value switch
			{
				"cover" => (1920, 360),
				"card" => (400, 140),
				"list" => (200, 110),
				"slimcover" => (240, 120),
				_ => (0, 0)
			};
			return true;
		}

		(width, height) = (0, 0);
		return false;
	}

	[GeneratedRegex(@"^/beatmapsets/(?<mapsetId>\d+)/(?<beatmapId>\d+)/background$")]
	private static partial Regex BeatmapBackgroundRegex();

	[GeneratedRegex(@"^/beatmapsets/(?<mapsetId>\d+)/background$")]
	private static partial Regex MapsetBackgroundRegex();

	[GeneratedRegex(@"^/beatmapsets/(?<mapsetId>\d+)/covers/(?<variant>cover|card|list|slimcover)\.jpg$")]
	private static partial Regex CoverRegex();
}