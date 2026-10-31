using Basil.Infrastructure.Common.Persistence;

namespace Basil.Infrastructure.Common.Registry;

/// <summary>Base for repositories whose items live in memory, the source of truth, with the database keeping a copy.</summary>
/// <typeparam name="TKey">The identity of an item.</typeparam>
/// <typeparam name="T">The type of each item.</typeparam>
/// <param name="reader">The database reader.</param>
/// <param name="writer">The database writer.</param>
public abstract class MemoryRepository<TKey, T>(DatabaseReader reader, DatabaseWriter writer)
	where TKey : notnull where T : class
{
	private long _removals;

	/// <summary>Gets the live instances held by the repository.</summary>
	protected IdentityMap<TKey, T> Items { get; } = new();

	/// <summary>Gets the reader used to load stored items.</summary>
	protected DatabaseReader Reader { get; } = reader;

	/// <summary>Gets the writer used to store item changes.</summary>
	protected DatabaseWriter Writer { get; } = writer;

	/// <summary>Gets a value that changes after an item removal commits.</summary>
	protected long RemovalGeneration => Volatile.Read(ref _removals);

	/// <summary>Gets the identity of an item.</summary>
	/// <param name="item">The item.</param>
	/// <returns>The item's identity.</returns>
	protected abstract TKey KeyOf(T item);

	/// <summary>Gets the database write order identity of an item.</summary>
	/// <param name="item">The item.</param>
	/// <returns>The item's write root.</returns>
	protected abstract Root RootOf(T item);

	/// <summary>Loads a stored item that is not in memory, or null when none is stored.</summary>
	/// <param name="key">The item's identity.</param>
	/// <returns>The stored item, or <see langword="null" /> when none exists.</returns>
	protected abstract Task<T?> LoadAsync(TKey key);

	/// <summary>Makes the change that stores an item: it holds the item values as they are now, not the item itself.</summary>
	/// <param name="item">The item whose values are stored.</param>
	/// <returns>The command that stores the item's current values.</returns>
	protected abstract IDatabaseCommand SaveCommand(T item);

	/// <summary>Gives the live instance the values of another instance of the same item.</summary>
	/// <param name="live">The instance held by this repository.</param>
	/// <param name="from">The instance whose values are copied.</param>
	protected virtual void CopyTo(T live, T from)
	{
	}

	/// <summary>Records that a removal has committed; it runs before the removal's marker is dropped.</summary>
	protected void NoteRemovalCommitted()
	{
		Interlocked.Increment(ref _removals);
	}

	/// <summary>Tells whether an item is being removed, so that it is not loaded again.</summary>
	/// <param name="key">The item's identity.</param>
	/// <returns><see langword="true" /> when removal is in progress.</returns>
	protected virtual bool IsRemoving(TKey key)
	{
		return false;
	}

	/// <summary>Called when an item is saved, so that a removal still in flight no longer holds it out.</summary>
	/// <param name="key">The item's identity.</param>
	protected virtual void CancelRemoval(TKey key)
	{
	}

	/// <summary>Gets a live item, loading it when needed, unless a removal is in progress.</summary>
	/// <param name="key">The item's identity.</param>
	/// <param name="cancellationToken">A token that cancels this caller's wait.</param>
	/// <returns>The live item, or <see langword="null" /> when it is not available.</returns>
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

	/// <summary>Keeps the item in memory unless it is currently being removed.</summary>
	/// <param name="item">The item.</param>
	/// <returns>The live instance associated with the item's identity.</returns>
	protected T Track(T item)
	{
		return IsBeingRemoved(item) ? item : Items.GetOrAdd(KeyOf(item), item);
	}

	/// <summary>Tells whether the item is currently being removed.</summary>
	/// <param name="item">The item.</param>
	/// <returns><see langword="true" /> when removal is in progress.</returns>
	protected virtual bool IsBeingRemoved(T item)
	{
		return IsRemoving(KeyOf(item));
	}

	/// <summary>Stores the current state of an item: the live instance takes its values, and its copy is queued.</summary>
	/// <param name="item">The item whose state is stored.</param>
	/// <returns>A task that completes when the queued change is stored.</returns>
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