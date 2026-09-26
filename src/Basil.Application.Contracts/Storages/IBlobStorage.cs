namespace Basil.Application.Contracts.Storages;

/// <summary>
///     The single source of truth for one kind of file kept on disk, accessed as a stream so its
///     content is never fully materialized in memory or cached as an object.
/// </summary>
/// <remarks>
///     Once <see cref="SaveAsync" /> completes, the file is complete: every later
///     <see cref="OpenReadAsync" /> for the same key is guaranteed to see it. Every member acts on
///     exactly one item and is atomic.
/// </remarks>
/// <typeparam name="TKey">The type that uniquely identifies an item.</typeparam>
public interface IBlobStorage<in TKey>
{
	/// <summary>Opens the file identified by <paramref name="key" /> for reading.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the open.</param>
	/// <returns>
	///     A readable stream positioned at the start of the file, or <see langword="null" /> when no
	///     file exists for <paramref name="key" />. The caller owns the stream and must dispose it.
	/// </returns>
	ValueTask<Stream?> OpenReadAsync(TKey key, CancellationToken cancellationToken = default);

	/// <summary>Writes <paramref name="content" /> to disk, replacing any existing file for the same key.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="content">
	///     The content to store. The implementation reads it to completion but does not take ownership
	///     of it and does not dispose it.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(TKey key, Stream content, CancellationToken cancellationToken = default);

	/// <summary>Deletes the file for <paramref name="key" />, if any.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the delete.</param>
	Task DeleteAsync(TKey key, CancellationToken cancellationToken = default);
}
