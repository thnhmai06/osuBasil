using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Basil.Infrastructure.Storage.Memory;

/// <summary>Keeps the items each owner has (a user's restrictions, a set's beatmaps) in step with their changes.</summary>
/// <remarks>
///     A list lives as long as the live instance of its owner. A list is loaded from what is committed, so a change
///     still queued, or made while the list is loading, would be missing from it: every change is applied to the loaded
///     list, kept until its write is committed so a later load applies it too, and a load that overlapped a change
///     starts again. Changes must be idempotent (add or replace by id, remove by id).
/// </remarks>
internal sealed class OwnedLists<TKey, TOwner, T> where TKey : notnull where TOwner : class
{
	private readonly Lock _gate = new();
	private readonly ConditionalWeakTable<TOwner, Holder> _lists = new();
	private readonly Dictionary<TKey, List<Load>> _loading = new();
	private readonly Dictionary<TKey, List<Func<ImmutableList<T>, ImmutableList<T>>>> _uncommitted = new();

	/// <summary>Gets an owner's list, loading it when it is not in memory.</summary>
	/// <param name="key">The owner's key.</param>
	/// <param name="owner">The live instance of the owner; the list lives as long as it does.</param>
	/// <param name="load">Reads the owner's committed items.</param>
	public async ValueTask<ImmutableList<T>> GetOrLoadAsync(TKey key, TOwner owner, Func<Task<ImmutableList<T>>> load)
	{
		while (true)
		{
			if (_lists.TryGetValue(owner, out var holder))
				return holder.List;

			var ticket = new Load();
			lock (_gate)
			{
				Add(_loading, key, ticket);
			}

			ImmutableList<T> stored;
			try
			{
				stored = await load();
			}
			finally
			{
				lock (_gate)
				{
					Remove(_loading, key, ticket);
				}
			}

			lock (_gate)
			{
				if (ticket.Changed)
					continue;

				if (_uncommitted.TryGetValue(key, out var changes))
					stored = changes.Aggregate(stored, (list, change) => change(list));
				if (_lists.TryGetValue(owner, out var existing))
					return existing.List;

				_lists.Add(owner, new Holder(stored));
				return stored;
			}
		}
	}

	/// <summary>Applies a change to an owner's list, now if it is loaded and to any later load until the change is committed.</summary>
	/// <param name="key">The owner's key.</param>
	/// <param name="owner">
	///     The live instance of the owner, or null when the caller does not hold it (then only later loads see
	///     the change).
	/// </param>
	/// <param name="change">The idempotent change.</param>
	/// <param name="committed">Completes once the change is in the database.</param>
	public void Change(TKey key, TOwner? owner, Func<ImmutableList<T>, ImmutableList<T>> change, Task committed)
	{
		lock (_gate)
		{
			Overlap(key);
			if (owner is not null && _lists.TryGetValue(owner, out var holder))
				holder.List = change(holder.List);
			if (committed.IsCompleted)
				return;

			Add(_uncommitted, key, change);
		}

		_ = committed.ContinueWith(_ =>
		{
			lock (_gate)
			{
				// A load running now may have read before the commit; with the change gone it would miss it.
				Remove(_uncommitted, key, change);
				Overlap(key);
			}
		}, TaskScheduler.Default);
	}

	/// <summary>Makes every load of an owner in progress start again.</summary>
	private void Overlap(TKey key)
	{
		if (_loading.TryGetValue(key, out var loads))
			foreach (var load in loads)
				load.Changed = true;
	}

	private static void Add<TItem>(Dictionary<TKey, List<TItem>> lists, TKey key, TItem item)
	{
		if (!lists.TryGetValue(key, out var items))
			lists[key] = items = [];
		items.Add(item);
	}

	private static void Remove<TItem>(Dictionary<TKey, List<TItem>> lists, TKey key, TItem item)
	{
		if (lists.TryGetValue(key, out var items) && items.Remove(item) && items.Count == 0)
			lists.Remove(key);
	}

	private sealed class Holder(ImmutableList<T> list)
	{
		public ImmutableList<T> List { get; set; } = list;
	}

	/// <summary>A load in progress, and whether a change overlapped it.</summary>
	private sealed class Load
	{
		public bool Changed { get; set; }
	}
}