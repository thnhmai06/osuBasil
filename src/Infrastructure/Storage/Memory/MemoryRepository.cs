using System.Collections.Concurrent;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Writing;

namespace Basil.Infrastructure.Storage.Memory;

/// <summary>Base for repositories whose items live in memory, the source of truth, with the database keeping a copy.</summary>
internal abstract class MemoryRepository<TKey, T>(Database database, DatabaseWriter writer)
	where TKey : notnull where T : class
{
	protected IdentityMap<TKey, T> Items { get; } = new();
	private readonly ConcurrentDictionary<TKey, Task> _removing = new();
	protected Database Database { get; } = database;
	protected DatabaseWriter Writer { get; } = writer;

	protected abstract TKey KeyOf(T item);
	protected abstract Root RootOf(T item);
	/// <summary>Loads a stored item that is not in memory, or null when none is stored.</summary>
	protected abstract Task<T?> LoadAsync(TKey key);
	protected abstract string WriteSql { get; }
	protected abstract object WriteParameters(T item);
	protected virtual WriteCommand EraseCommand(TKey key) => throw new NotSupportedException();
	/// <summary>Gives the live instance the values of another instance of the same item.</summary>
	protected virtual void CopyTo(T live, T from) { }

	protected ValueTask<T?> FindAsync(TKey key, CancellationToken cancellationToken) =>
		_removing.ContainsKey(key)
			? ValueTask.FromResult<T?>(null)
			: Items.GetOrAddAsync(key, async miss =>
				await LoadAsync(miss) is { } loaded && !IsBeingRemoved(loaded) ? loaded : null, cancellationToken);

	protected T Track(T item) => IsBeingRemoved(item) ? item : Items.GetOrAdd(KeyOf(item), item);
	protected virtual bool IsBeingRemoved(T item) => _removing.ContainsKey(KeyOf(item));

	/// <summary>Stores the current state of an item: the live instance takes its values, and its copy is queued.</summary>
	protected Task SaveAsync(T item)
	{
		var key = KeyOf(item);
		_removing.TryRemove(key, out _);
		var live = Track(item);
		if (!ReferenceEquals(live, item)) CopyTo(live, item);
		var stored = Writer.SaveAsync(RootOf(live), (GetType(), key), new WriteCommand(WriteSql, WriteParameters(live)));
		Items.Pin(live, stored);
		return stored;
	}

	/// <summary>Forgets an item and queues its deletion; until the deletion is committed the item is not loaded again.</summary>
	protected Task RemoveAsync(T item)
	{
		var key = KeyOf(item);
		var erased = Writer.SaveAsync(RootOf(item), (GetType(), key), EraseCommand(key));
		_removing[key] = erased;
		Items.Remove(key);
		_ = erased.ContinueWith(_ => _removing.TryRemove(new KeyValuePair<TKey, Task>(key, erased)), TaskScheduler.Default);
		return erased;
	}
}
