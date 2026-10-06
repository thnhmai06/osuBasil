using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Mechanics;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Services.Beatmaps;

/// <inheritdoc />
/// <remarks>
///     Reads the de facto osu!direct mirror search contract (Chimu / Nerinyan / osu.direct-style
///     APIs, the same shape bancho.py itself consumes): a JSON array of sets, each with a
///     <c>ChildrenBeatmaps</c> array. <c>HasVideo</c> is read leniently as either a JSON boolean or
///     a <c>0</c>/<c>1</c> number, since mirrors are inconsistent about which they send.
/// </remarks>
internal sealed class HttpBeatmapsetMirror(
	ISettingsRepository settings,
	ILogger<HttpBeatmapsetMirror> logger) : IBeatmapsetMirror
{
	private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
	{
		Timeout = TimeSpan.FromSeconds(10)
	};

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <inheritdoc />
	public async Task<IReadOnlyList<MirrorBeatmapset>?> SearchAsync(string? text, GameMode? mode, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var serverSettings = await settings.GetAsync(cancellationToken);
		var endpoint = serverSettings.MirrorSearchEndpoint;
		if (endpoint is null) return null;

		var separator = endpoint.ToString().Contains('?') ? '&' : '?';
		var url = $"{endpoint}{separator}amount={page.Limit}&offset={page.Offset}";
		if (!string.IsNullOrEmpty(text)) url += $"&query={Uri.EscapeDataString(text)}";
		if (mode is not null) url += $"&mode={(int)mode}";

		try
		{
			var payload = await Http.GetFromJsonAsync<List<RawSet>>(url, JsonOptions, cancellationToken);
			return payload?.Where(set => set.ChildrenBeatmaps is not null).Select(ToBeatmapset).ToList();
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
		{
			logger.LogWarning(ex, "Mirror search request failed: {Url}", url);
			return null;
		}
	}

	/// <inheritdoc />
	public async Task<Uri?> GetDownloadAddressAsync(int beatmapsetId, bool withVideo,
		CancellationToken cancellationToken = default)
	{
		var serverSettings = await settings.GetAsync(cancellationToken);
		var endpoint = serverSettings.MirrorDownloadEndpoint;
		if (endpoint is null) return null;

		var baseAddress = endpoint.ToString().TrimEnd('/');
		return new Uri($"{baseAddress}/{beatmapsetId}?n={(withVideo ? 0 : 1)}");
	}

	/// <summary>Maps the wire set to the contract model.</summary>
	private static MirrorBeatmapset ToBeatmapset(RawSet set)
	{
		return new MirrorBeatmapset(
			set.SetId,
			set.Artist,
			set.Title,
			set.Creator,
			ParseUpdatedAt(set.LastUpdate),
			ParseHasVideo(set.HasVideo),
			set.ChildrenBeatmaps?.Select(ToBeatmap).ToList() ?? []);
	}

	/// <summary>Maps the wire beatmap to the contract model.</summary>
	private static MirrorBeatmap ToBeatmap(RawBeatmap beatmap)
	{
		return new MirrorBeatmap(beatmap.BeatmapId, beatmap.DiffName, (GameMode)beatmap.Mode, beatmap.DifficultyRating);
	}

	/// <summary>Tolerates Unix-second or Unix-millisecond timestamps and unparseable inputs.</summary>
	private static DateTimeOffset ParseUpdatedAt(string value)
	{
		if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var raw))
		{
			var ms = raw > 1_000_000_000_000 ? raw : raw * 1000;
			return DateTimeOffset.FromUnixTimeMilliseconds(ms);
		}

		return DateTimeOffset.UnixEpoch;
	}

	/// <summary>Reads <c>HasVideo</c> as either a JSON boolean or a <c>0</c>/<c>1</c> number.</summary>
	private static bool ParseHasVideo(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.Number => element.TryGetInt32(out var n) && n != 0,
			_ => false
		};
	}

	private sealed class RawSet
	{
		public string Artist { get; init; } = "";
		public string Title { get; init; } = "";
		public string Creator { get; init; } = "";
		public string LastUpdate { get; init; } = "";
		public int SetId { get; init; }
		public JsonElement HasVideo { get; init; }
		public List<RawBeatmap>? ChildrenBeatmaps { get; init; }
	}

	private sealed class RawBeatmap
	{
		public int BeatmapId { get; init; }
		public string DiffName { get; init; } = "";
		public double DifficultyRating { get; init; }
		public int Mode { get; init; }
	}
}