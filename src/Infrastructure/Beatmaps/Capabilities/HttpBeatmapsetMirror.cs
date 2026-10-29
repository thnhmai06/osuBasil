using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Common;
using Basil.Domain.Mechanics;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps.Capabilities;

/// <inheritdoc />
/// <remarks>
///     Reads the de facto osu!direct mirror search contract (Chimu / Nerinyan / osu.direct-style
///     APIs, the same shape bancho.py itself consumes): a JSON array of sets, each with a
///     <c>ChildrenBeatmaps</c> array. <c>HasVideo</c> is read leniently as either a JSON boolean or
///     a <c>0</c>/<c>1</c> number, since mirrors are inconsistent about which they send.
/// </remarks>
internal sealed class HttpBeatmapsetMirror : IBeatmapsetMirror
{
	private static readonly HttpClient Http =
		new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
		{
			Timeout = TimeSpan.FromSeconds(10)
		};

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly HttpClient _http;
	private readonly ILogger<HttpBeatmapsetMirror> _logger;

	private readonly IBeatmapMirrorSettings _settings;

	/// <summary>Creates the mirror client that reaches the configured mirror.</summary>
	/// <param name="settings">The source of the mirror endpoints in effect for each request.</param>
	/// <param name="logger">The logger failures are reported to.</param>
	public HttpBeatmapsetMirror(IBeatmapMirrorSettings settings, ILogger<HttpBeatmapsetMirror> logger)
		: this(settings, logger, Http)
	{
	}

	/// <summary>Creates the mirror client that sends its requests through the given client.</summary>
	/// <param name="settings">The source of the mirror endpoints in effect for each request.</param>
	/// <param name="logger">The logger failures are reported to.</param>
	/// <param name="http">The client the requests are sent through.</param>
	internal HttpBeatmapsetMirror(IBeatmapMirrorSettings settings, ILogger<HttpBeatmapsetMirror> logger, HttpClient http)
	{
		_settings = settings;
		_logger = logger;
		_http = http;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MirrorBeatmapset>?> SearchAsync(string? text, GameMode? mode, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (endpoint, _) = await _settings.GetAsync(cancellationToken);
		if (endpoint is null) return null;

		var url = SearchUrl(endpoint, text, mode, page);

		try
		{
			var payload = await _http.GetFromJsonAsync<List<RawSet>>(url, JsonOptions, cancellationToken);
			return payload?.Where(set => set.ChildrenBeatmaps is not null).Select(ToBeatmapset).ToList();
		}
		catch (Exception exception) when (
			exception is HttpRequestException or JsonException or TaskCanceledException &&
			!cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning(exception, "Mirror search request failed: {Endpoint}",
				endpoint.GetLeftPart(UriPartial.Path));
			return null;
		}
	}

	private static string SearchUrl(Uri endpoint, string? text, GameMode? mode, PageRequest page)
	{
		var separator = endpoint.ToString().Contains('?') ? '&' : '?';
		var url = $"{endpoint}{separator}amount={page.Limit}&offset={page.Offset}";
		if (!string.IsNullOrEmpty(text)) url += $"&query={Uri.EscapeDataString(text)}";
		if (mode is not null) url += $"&mode={(int)mode}";
		return url;
	}

	/// <inheritdoc />
	public async Task<Uri?> GetDownloadAddressAsync(int beatmapsetId, bool withVideo,
		CancellationToken cancellationToken = default)
	{
		var (_, endpoint) = await _settings.GetAsync(cancellationToken);
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

	/// <summary>Tolerates Unix-second or Unix-millisecond timestamps, ISO-8601 date strings and unparseable inputs.</summary>
	private static DateTimeOffset ParseUpdatedAt(string value)
	{
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
		{
			var milliseconds = raw > 1_000_000_000_000 ? raw : raw * 1000;
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
		}

		return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
			out var parsed)
			? parsed
			: DateTimeOffset.UnixEpoch;
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

	// Filled by the JSON serializer: the setters must stay.
	// Filled by the JSON serializer: the setters must stay.
	// Filled by the JSON serializer: the properties stay settable (init) although the IDE suggests get-only.
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