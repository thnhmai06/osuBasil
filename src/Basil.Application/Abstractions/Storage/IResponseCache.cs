namespace Basil.Application.Abstractions.Storage;

/// <summary>
///     Stores derived response bytes, such as resized thumbnails and transcoded audio previews,
///     that are expensive to recompute but cheap to keep.
/// </summary>
/// <remarks>
///     Entries are keyed by an endpoint name, a short label such as <c>"thumb"</c> or
///     <c>"preview"</c> rather than a full URL, plus a relative path underneath it, so an entry
///     lives at <c>{endpoint}/{relativePath}</c> within the cache root. A cache miss surfaces as
///     "not found" rather than an error, and the caller is expected to regenerate the bytes and
///     call <see cref="PutAsync" />.
/// </remarks>
public interface IResponseCache
{
	/// <summary>
	///     Reads a cached entry.
	/// </summary>
	/// <param name="endpoint">The short endpoint label the entry was stored under.</param>
	/// <param name="relativePath">The entry's path within the endpoint.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The cached bytes, or <see langword="null" /> when nothing is cached at that key.</returns>
	Task<byte[]?> GetAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default);

	/// <summary>
	///     Stores an entry, overwriting anything already cached at the same key.
	/// </summary>
	/// <param name="endpoint">The short endpoint label to store under.</param>
	/// <param name="relativePath">The entry's path within the endpoint.</param>
	/// <param name="content">The bytes to cache.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task PutAsync(string endpoint, string relativePath, byte[] content, CancellationToken cancellationToken = default);

	/// <summary>
	///     Removes a cached entry.
	/// </summary>
	/// <param name="endpoint">The short endpoint label of the entry to remove.</param>
	/// <param name="relativePath">The entry's path within the endpoint.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     Deleting an entry that is not cached is a no-op, not an error.
	/// </remarks>
	Task DeleteAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
///     Builds the cache keys used by the resize and transcode caches.
/// </summary>
/// <remarks>
///     Shared between the services that populate the caches (thumbnail and audio-preview
///     generation) and <c>BeatmapIngestionService</c>, which invalidates the caches when a mapset is
///     deleted. Living in this layer rather than in either producer keeps both in sync without a
///     cross-layer reference.
/// </remarks>
public static class ResponseCacheKeys
{
	/// <summary>
	///     Builds the cache key for a mapset's thumbnail.
	/// </summary>
	/// <param name="mapsetId">The id of the mapset.</param>
	/// <param name="large">
	///     <see langword="true" /> for the large 160x120 thumbnail; otherwise, <see langword="false" />
	///     for the small 80x60 one.
	/// </param>
	/// <returns>The relative cache path for the thumbnail.</returns>
	public static string Thumb(int mapsetId, bool large)
	{
		return large ? $"{mapsetId}l.jpg" : $"{mapsetId}.jpg";
	}

	/// <summary>
	///     Builds the cache key for a mapset's audio preview.
	/// </summary>
	/// <param name="mapsetId">The id of the mapset.</param>
	/// <returns>The relative cache path for the audio preview.</returns>
	public static string Preview(int mapsetId)
	{
		return $"{mapsetId}.mp3";
	}
}