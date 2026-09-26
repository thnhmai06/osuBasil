namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     The single source of truth for reading, writing, and deleting one durably-persisted item by
///     its key.
/// </summary>
/// <remarks>
///     Once <see cref="SaveAsync" /> completes, the item is durable: every later
///     <see cref="LoadAsync" /> for the same key is guaranteed to see it. Every member acts on
///     exactly one item and is atomic.
/// </remarks>
/// <typeparam name="TKey">The type that uniquely identifies an item.</typeparam>
/// <typeparam name="T">The type of item stored.</typeparam>
public interface IRepository<in TKey, T> where T : notnull
{
	/// <summary>Loads the item identified by <paramref name="key" />.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The item, or <see langword="null" /> when no item exists for <paramref name="key" />.</returns>
	ValueTask<T?> LoadAsync(TKey key, CancellationToken cancellationToken = default);

	/// <summary>Durably stores <paramref name="item" />, replacing any existing item with the same key.</summary>
	/// <param name="item">The item to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(T item, CancellationToken cancellationToken = default);

	/// <summary>Deletes the item identified by <paramref name="key" />, if any.</summary>
	/// <param name="key">The item's key.</param>
	/// <param name="cancellationToken">A token that cancels the delete.</param>
	Task DeleteAsync(TKey key, CancellationToken cancellationToken = default);
}