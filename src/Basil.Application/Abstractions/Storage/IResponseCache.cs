namespace Basil.Application.Abstractions.Storage;

/// <summary>
///     Cache for derived response bytes (resized thumbnails, transcoded audio previews) that are
///     expensive to (re)compute but cheap to store — keyed by an endpoint name (a short label like
///     <c>"thumb"</c>/<c>"preview"</c>, not a full URL) plus a relative path under it. Backed by
///     <c>StorageOptions.CachePath</c> on disk (<c>{CachePath}/{endpoint}/{relativePath}</c>); a
///     cache miss is simply "not found", never an error — the caller regenerates and calls
///     <see cref="PutAsync" />.
/// </summary>
public interface IResponseCache
{
	Task<byte[]?> GetAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default);

	Task PutAsync(string endpoint, string relativePath, byte[] content, CancellationToken cancellationToken = default);

	/// <summary>No-op (not an error) if the entry isn't cached.</summary>
	Task DeleteAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
///     Cache key builders for the `b.` host's resize/transcode caches — shared between the routes that
///     populate them (<c>BanchoHostGroups</c>'s thumbnail/preview handlers) and
///     <c>BeatmapIngestionService</c>, which invalidates them when a mapset is deleted. Living here
///     (Application) rather than in either producer keeps both in sync without a Web→Infrastructure or
///     Infrastructure→Web reference.
/// </summary>
public static class ResponseCacheKeys
{
	public static string Thumb(int mapsetId, bool large)
	{
		return large ? $"{mapsetId}l.jpg" : $"{mapsetId}.jpg";
	}

	public static string Preview(int mapsetId)
	{
		return $"{mapsetId}.mp3";
	}
}
