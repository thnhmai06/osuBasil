using System.Collections.Immutable;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Keeps the items each owner has (a user's restrictions, a set's beatmaps) in step with their changes.</summary>
/// <remarks>
///     A list is loaded from what is committed, so a change that is still queued, or that happens while the list is
///     being loaded, would be missing from it. Every change is therefore applied to the loaded list, kept until its
///     write is committed so a later load applies it too, and a load that overlapped a change starts again. Changes
///     must be idempotent (add or replace by id, remove by id).
/// </remarks>
internal sealed class OwnedLists<TOwner, T> where TOwner : notnull
{
	private readonly IdentityMap<TOwner, ImmutableList<T>> _lists = new();
	private readonly Lock _gate = new();
	private readonly Dictionary<TOwner, List<Func<ImmutableList<T>, ImmutableList<T>>>> _uncommitted = new();
	private readonly Dictionary<TOwner, List<Load>> _loading = new();

	/// <summary>Gets an owner's list, loading it when it is not in memory.</summary>
	/// <param name="owner">The owner.</param>
	/// <param name="load">Reads the owner's committed items.</param>
	public async ValueTask<ImmutableList<T>> GetOrLoadAsync(TOwner owner, Func<Task<ImmutableList<T>>> load)
	{
		while (true)
		{
			if (_lists.TryGetValue(owner, out var cached))
				return cached;

			var ticket = new Load();
			lock (_gate)
				Add(_loading, owner, ticket);

			ImmutableList<T> stored;
			try
			{
				stored = await load();
			}
			finally
			{
				lock (_gate)
					Remove(_loading, owner, ticket);
			}

			lock (_gate)
			{
				if (ticket.Changed)
					continue;

				if (_uncommitted.TryGetValue(owner, out var changes))
					stored = changes.Aggregate(stored, (list, change) => change(list));
				return _lists.GetOrAdd(owner, stored);
			}
		}
	}

	/// <summary>Applies a change to an owner's list, now and to any load of it until the change is committed.</summary>
	/// <param name="owner">The owner.</param>
	/// <param name="change">The idempotent change.</param>
	/// <param name="committed">Completes once the change is in the database.</param>
	public void Change(TOwner owner, Func<ImmutableList<T>, ImmutableList<T>> change, Task committed)
	{
		lock (_gate)
		{
			if (_loading.TryGetValue(owner, out var loads))
				foreach (var load in loads)
					load.Changed = true;

			_lists.TryUpdate(owner, change);
			if (committed.IsCompleted)
				return;

			Add(_uncommitted, owner, change);
		}

		committed.ContinueWith(_ =>
		{
			lock (_gate)
				Remove(_uncommitted, owner, change);
		}, TaskScheduler.Default);
	}

	private static void Add<TItem>(Dictionary<TOwner, List<TItem>> lists, TOwner owner, TItem item)
	{
		if (!lists.TryGetValue(owner, out var items))
			lists[owner] = items = [];
		items.Add(item);
	}

	private static void Remove<TItem>(Dictionary<TOwner, List<TItem>> lists, TOwner owner, TItem item)
	{
		if (lists.TryGetValue(owner, out var items) && items.Remove(item) && items.Count == 0)
			lists.Remove(owner);
	}

	/// <summary>A load in progress, and whether a change overlapped it.</summary>
	private sealed class Load
	{
		public bool Changed { get; set; }
	}
}
