using System.Collections.Concurrent;
using Basil.Infrastructure.Persistence.Postgres;

namespace Basil.Infrastructure.Persistence.Memory;

/// <summary>
///     Base for memory repositories whose items can be deleted: a deleted item is not loaded again once its deletion
///     is queued.
/// </summary>
internal abstract class RemovableMemoryRepository<TKey, T>(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<TKey, T>(reader, writer) where TKey : notnull where T : class
{
	private readonly ConcurrentDictionary<TKey, Task> _removing = new();

	/// <summary>Makes the change that deletes the item stored under a key.</summary>
	protected abstract WriteCommand EraseCommand(TKey key);

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

	/// <summary>Forgets an item and queues its deletion; until the deletion is committed the item is not loaded again.</summary>
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