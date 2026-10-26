using Basil.Infrastructure.Persistence.Postgres;

namespace Basil.Infrastructure.Persistence.Memory;

/// <summary>Base for repositories whose items live in memory, the source of truth, with the database keeping a copy.</summary>
internal abstract class MemoryRepository<TKey, T>(DatabaseReader reader, DatabaseWriter writer)
	where TKey : notnull where T : class
{
	private long _removals;
	protected IdentityMap<TKey, T> Items { get; } = new();
	protected DatabaseReader Reader { get; } = reader;
	protected DatabaseWriter Writer { get; } = writer;

	/// <summary>Counts the removals that have committed; a read compares it before and after it queries.</summary>
	protected long RemovalGeneration => Volatile.Read(ref _removals);

	protected abstract TKey KeyOf(T item);
	protected abstract Root RootOf(T item);

	/// <summary>Loads a stored item that is not in memory, or null when none is stored.</summary>
	protected abstract Task<T?> LoadAsync(TKey key);

	/// <summary>Makes the change that stores an item: it holds the item values as they are now, not the item itself.</summary>
	protected abstract WriteCommand SaveCommand(T item);

	/// <summary>Gives the live instance the values of another instance of the same item.</summary>
	protected virtual void CopyTo(T live, T from)
	{
	}

	/// <summary>Records that a removal has committed; it runs before the removal's marker is dropped.</summary>
	protected void NoteRemovalCommitted()
	{
		Interlocked.Increment(ref _removals);
	}

	/// <summary>Tells whether a stored item is being removed, so that it is not loaded into memory again.</summary>
	protected virtual bool IsRemoving(TKey key)
	{
		return false;
	}

	/// <summary>Called when an item is saved, so that a removal still in flight for its key no longer holds it out.</summary>
	protected virtual void CancelRemoval(TKey key)
	{
	}

	protected ValueTask<T?> FindAsync(TKey key, CancellationToken cancellationToken)
	{
		return IsRemoving(key)
			? ValueTask.FromResult<T?>(null)
			: Items.GetOrAddAsync(key, LoadUnlessRemovedAsync, cancellationToken);
	}

	/// <summary>Reads a value, reading it again when a removal committed while the read was running.</summary>
	/// <param name="read">Reads the value.</param>
	/// <returns>The value of a read that no removal committed during.</returns>
	protected async Task<TResult> ReadUntilStableAsync<TResult>(Func<Task<TResult>> read)
	{
		while (true)
		{
			var generation = RemovalGeneration;
			var result = await read();
			if (generation == RemovalGeneration)
				return result;
		}
	}

	/// <summary>Loads an item, starting again when a removal committed while the load was running.</summary>
	private Task<T?> LoadUnlessRemovedAsync(TKey key)
	{
		return ReadUntilStableAsync<T?>(async () =>
		{
			if (await LoadAsync(key) is not { } loaded || IsBeingRemoved(loaded))
				return null;

			return loaded;
		});
	}

	protected T Track(T item)
	{
		return IsBeingRemoved(item) ? item : Items.GetOrAdd(KeyOf(item), item);
	}

	protected virtual bool IsBeingRemoved(T item)
	{
		return IsRemoving(KeyOf(item));
	}

	/// <summary>Stores the current state of an item: the live instance takes its values, and its copy is queued.</summary>
	protected Task SaveAsync(T item)
	{
		var key = KeyOf(item);
		CancelRemoval(key);
		var live = Track(item);
		if (!ReferenceEquals(live, item)) CopyTo(live, item);
		var stored = Writer.SaveAsync(RootOf(live), (GetType(), key),
			SaveCommand(live));
		Items.Pin(live, stored);
		return stored;
	}
}