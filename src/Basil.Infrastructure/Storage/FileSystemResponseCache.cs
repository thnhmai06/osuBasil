using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage;

/// <inheritdoc cref="IResponseCache" />
/// <remarks>
///     Stores entries on disk under <see cref="StorageOptions.CachePath" /> as
///     <c>{endpoint}/{relativePath}</c>. A read of a key with no stored file returns null rather
///     than throwing; a deleted of a missing key is a no-op.
/// </remarks>
public sealed class FileSystemResponseCache(IOptions<StorageOptions> options) : IResponseCache
{
	/// <inheritdoc />
	public async Task<byte[]?> GetAsync(string endpoint, string relativePath,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
	}

	/// <inheritdoc />
	/// <remarks>
	///     Creates the entry's parent folders when they do not yet exist, then writes to a
	///     sibling temp file and renames it into place. The rename (not a direct write to
	///     <paramref name="relativePath" />'s final path) is what makes this atomic: a concurrent
	///     <see cref="GetAsync" /> for the same key always observes either the previous file or
	///     the complete new one, never a partially-written one from two regenerations racing on a
	///     cache miss.
	/// </remarks>
	public async Task PutAsync(string endpoint, string relativePath, byte[] content,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
		await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
		File.Move(tempPath, path, true);
	}

	/// <inheritdoc />
	/// <remarks>Deleting an entry that is not cached is a no-op.</remarks>
	public Task DeleteAsync(string endpoint, string relativePath, CancellationToken cancellationToken = default)
	{
		var path = PathFor(endpoint, relativePath);
		if (File.Exists(path)) File.Delete(path);
		return Task.CompletedTask;
	}

	/// <summary>Builds the absolute cache path for an endpoint and relative path.</summary>
	/// <param name="endpoint">The short endpoint label.</param>
	/// <param name="relativePath">The entry's path within the endpoint.</param>
	/// <returns>The absolute path of the cache file.</returns>
	private string PathFor(string endpoint, string relativePath)
	{
		return Path.Combine(options.Value.CachePath, endpoint, relativePath);
	}
}