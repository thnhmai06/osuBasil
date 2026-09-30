namespace Basil.Application.Common.Persistence;

/// <summary>
///     The single source of truth for reading, writing, and deleting one durably-persisted item by
///     its key.
/// </summary>
/// <remarks>
///     Once <see cref="SaveAsync" /> completes, the item is durable: every later
///     <see cref="GetAsync" /> for the same key is guaranteed to see it. Every member acts on
///     exactly one item and is atomic.
/// </remarks>
/// <typeparam name="TKey">The type that uniquely identifies an item.</typeparam>
/// <typeparam name="TValue">The type of item stored.</typeparam>
public interface IRepository<in TKey, TValue> where TKey : notnull where TValue : notnull
{
	/// <summary>Loads the item identified by <paramref name="key" />.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The item, or <see langword="null" /> when no item exists for <paramref name="key" />.</returns>
	ValueTask<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

	/// <summary>Writes an item whose identity is already known, replacing any stored item with the same key.</summary>
	/// <param name="value">The item to write.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(TValue value, CancellationToken cancellationToken = default);

	/// <summary>Deletes the item identified by <paramref name="key" />, if any.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the delete.</param>
	Task DeleteAsync(TKey key, CancellationToken cancellationToken = default);
}