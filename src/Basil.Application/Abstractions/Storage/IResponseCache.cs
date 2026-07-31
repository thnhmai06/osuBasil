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
}
