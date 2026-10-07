using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Batching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Base for repositories whose stored items stay live in memory once read.</summary>
internal abstract class CachedRepository<TKey, T>(DatabaseBatcher batcher)
	where TKey : notnull where T : class
{
	protected IdentityMap<TKey, T> Items { get; } = new();

	// Reads see only committed rows, so a row whose deletion is still queued must not come back into memory.
	private readonly ConcurrentSet<TKey> _removing = [];

	/// <summary>Gets the batcher that runs this repository's reads and writes.</summary>
	protected DatabaseBatcher Batcher { get; } = batcher;

	protected abstract TKey KeyOf(T item);

	/// <summary>Loads a stored item that is not in memory yet, or <see langword="null" /> when none is stored.</summary>
	protected abstract Task<T?> LoadAsync(TKey key, CancellationToken cancellationToken);

	/// <summary>Gets the statement that stores an item: an insert of its key that updates the stored row when one exists.</summary>
	protected abstract string WriteSql { get; }

	/// <summary>Gets the values the statement stores for an item, as they are when the change is queued.</summary>
	protected abstract object WriteParameters(T item);

	protected virtual Task EraseAsync(SqliteConnection connection, SqliteTransaction transaction, TKey key)
		=> throw new NotSupportedException();

	protected ValueTask<T?> FindAsync(TKey key, CancellationToken cancellationToken) =>
		_removing.Contains(key)
			? ValueTask.FromResult<T?>(null)
			: Items.GetOrAddAsync(key, async miss =>
				await LoadAsync(miss, cancellationToken) is { } loaded && !IsBeingRemoved(loaded) ? loaded : null);

	/// <summary>Gets the live instance of an item, keeping it when it is the first; an item being deleted is not kept.</summary>
	protected T Track(T item) => IsBeingRemoved(item) ? item : Items.GetOrAdd(KeyOf(item), item);

	/// <summary>Tells whether the deletion of an item is queued but not committed yet.</summary>
	protected virtual bool IsBeingRemoved(T item) => _removing.Contains(KeyOf(item));

	/// <summary>Queues the state the live instance of an item has now.</summary>
	/// <returns>A task that completes once the state is committed.</returns>
	protected Task SaveAsync(T item)
	{
		var live = Track(item);
		var parameters = WriteParameters(live);
		return Batcher.EnqueueAsync((GetType(), KeyOf(live)),
			(connection, transaction) => connection.ExecuteAsync(WriteSql, parameters, transaction));
	}

	/// <summary>Forgets an item and queues its deletion.</summary>
	/// <returns>A task that completes once the deletion is committed.</returns>
	protected Task RemoveAsync(T item)
	{
		var key = KeyOf(item);
		_removing.Add(key);
		Items.Remove(key);
		var erased = Batcher.EnqueueAsync((GetType(), key),
			(connection, transaction) => EraseAsync(connection, transaction, key));
		erased.ContinueWith(_ => _removing.Remove(key), TaskScheduler.Default);
		return erased;
	}
}
