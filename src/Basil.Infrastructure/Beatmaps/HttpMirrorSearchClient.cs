using System.Net.Http.Json;
using System.Text.Json;
using Basil.Application.Abstractions.Beatmaps;
using Microsoft.Extensions.Logging;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable CollectionNeverUpdated.Local

namespace Basil.Infrastructure.Beatmaps;

/// <inheritdoc cref="IMirrorSearchClient" />
/// <remarks>
///     Matches the de facto osu!direct mirror search contract (Chimu/Nerinyan/osu.direct-style
///     APIs, the same shape bancho.py itself consumes): a JSON array of sets, each with a
///     <c>ChildrenBeatmaps</c> array. <c>HasVideo</c> is read leniently as either a JSON boolean or
///     a <c>0</c>/<c>1</c> number, since mirrors are inconsistent about which they send.
/// </remarks>
public sealed class HttpMirrorSearchClient(HttpClient httpClient, ILogger<HttpMirrorSearchClient> logger)
	: IMirrorSearchClient
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <inheritdoc />
	public async Task<IReadOnlyList<MirrorSearchSet>?> SearchAsync(string endpoint, string? query, int? mode,
		int amount, int offset, CancellationToken cancellationToken = default)
	{
		var queryParams = new List<string> { $"amount={amount}", $"offset={offset}" };
		if (!string.IsNullOrEmpty(query)) queryParams.Add($"query={Uri.EscapeDataString(query)}");
		if (mode is not null) queryParams.Add($"mode={mode}");
		var separator = endpoint.Contains('?') ? '&' : '?';
		var url = $"{endpoint}{separator}{string.Join('&', queryParams)}";

		try
		{
			using var response = await httpClient.GetAsync(url, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Mirror search returned {StatusCode}: {Url}", (int)response.StatusCode, url);
				return null;
			}

			var payload = await response.Content.ReadFromJsonAsync<List<RawSet>>(JsonOptions, cancellationToken);
			if (payload is null) return null;

			return payload.Where(set => set.ChildrenBeatmaps is not null).Select(ToMirrorSearchSet).ToList();
		}
		catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
		{
			logger.LogWarning(ex, "Mirror search request failed: {Url}", url);
			return null;
		}
	}

	private static MirrorSearchSet ToMirrorSearchSet(RawSet set)
	{
		return new MirrorSearchSet(set.Artist, set.Title, set.Creator, set.RankedStatus, set.LastUpdate, set.SetId,
			ParseHasVideo(set.HasVideo), set.ChildrenBeatmaps?.Select(ToMirrorSearchBeatmap).ToList());
	}

	private static MirrorSearchBeatmap ToMirrorSearchBeatmap(RawBeatmap beatmap)
	{
		return new MirrorSearchBeatmap(beatmap.DifficultyRating, beatmap.DiffName, beatmap.Cs, beatmap.Od,
			beatmap.Ar, beatmap.Hp, beatmap.Mode);
	}

	private static bool ParseHasVideo(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.Number => element.GetInt32() != 0,
			_ => false
		};
	}

	private sealed class RawSet
	{
		public string Artist { get; init; } = "";
		public string Title { get; init; } = "";
		public string Creator { get; init; } = "";
		public int RankedStatus { get; init; }
		public string LastUpdate { get; init; } = "";
		public int SetId { get; init; }
		public JsonElement HasVideo { get; init; }
		public List<RawBeatmap>? ChildrenBeatmaps { get; init; }
	}

	private sealed class RawBeatmap
	{
		public double DifficultyRating { get; init; }
		public string DiffName { get; init; } = "";
		public double Cs { get; init; }
		public double Od { get; init; }
		public double Ar { get; init; }
		public double Hp { get; init; }
		public int Mode { get; init; }
	}
}