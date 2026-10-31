using System.Collections.Concurrent;
using Basil.Infrastructure.Common.Persistence;

namespace Basil.Infrastructure.Common.Registry;

/// <summary>
///     Base for memory repositories whose items can be deleted: a deleted item is not loaded again once its deletion
///     is queued.
/// </summary>
/// <typeparam name="TKey">The identity of an item.</typeparam>
/// <typeparam name="T">The type of each item.</typeparam>
/// <param name="reader">The database reader.</param>
/// <param name="writer">The database writer.</param>
public abstract class RemovableMemoryRepository<TKey, T>(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<TKey, T>(reader, writer) where TKey : notnull where T : class
{
	private readonly ConcurrentDictionary<TKey, Task> _removing = new();

	/// <summary>Makes the change that deletes the item stored under a key.</summary>
	/// <param name="key">The item's identity.</param>
	/// <returns>The command that deletes the item.</returns>
	protected abstract IDatabaseCommand EraseCommand(TKey key);

	/// <inheritdoc />
	protected override bool IsRemoving(TKey key)
	{
		return _removing.ContainsKey(key);
	}

	/// <inheritdoc />
	protected override void CancelRemoval(TKey key)
	{
		_removing.TryRemove(key, out _);
	}

	/// <summary>Forgets an item and queues its deletion; until committed, the item is not loaded again.</summary>
	/// <param name="item">The item to remove.</param>
	/// <returns>A task that completes when the deletion is stored.</returns>
	protected Task RemoveAsync(T item)
	{
		var key = KeyOf(item);
		var erased = Writer.SaveAsync(RootOf(item), (GetType(), key), EraseCommand(key));
		_removing[key] = erased;
		Items.Remove(key);
		_ = erased.ContinueWith(_ =>
		{
			NoteRemovalCommitted();
			_removing.TryRemove(new KeyValuePair<TKey, Task>(key, erased));
		}, TaskScheduler.Default);
		return erased;
	}
}