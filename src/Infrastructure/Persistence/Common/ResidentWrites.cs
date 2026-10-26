namespace Basil.Infrastructure.Persistence.Common;

/// <summary>
///     Writes resident data to the database and changes the resident copy only after the write is committed, so that
///     of several concurrent changes of one identity the last one committed is the one memory holds.
/// </summary>
/// <typeparam name="TKey">The identity of a resident record.</typeparam>
/// <remarks>Creates the writes of one kind of resident record.</remarks>
/// <param name="comparer">How identities are compared, or <see langword="null" /> for the default.</param>
internal sealed class ResidentWrites<TKey>(IEqualityComparer<TKey>? comparer = null) where TKey : notnull
{
	private readonly Dictionary<TKey, long> _applied = new(comparer);
	private readonly Lock _gate = new();
	private long _issued;

	/// <summary>Queues a write and waits for its commit, then applies the change to the resident copy.</summary>
	/// <param name="key">The identity the write changes.</param>
	/// <param name="enqueue">Queues the write and returns the task that completes when it is committed.</param>
	/// <param name="apply">Changes the resident copy to what was committed.</param>
	/// <remarks>When the write fails the resident copy is left as it was and the failure reaches the caller.</remarks>
	public async Task WriteAsync(TKey key, Func<Task> enqueue, Action apply)
	{
		long turn;
		Task committed;
		lock (_gate)
		{
			// Writes of one root are committed in the order they are queued, so taking the turn and queueing happen together.
			turn = ++_issued;
			committed = enqueue();
		}

		await committed;
		lock (_gate)
		{
			if (_applied.TryGetValue(key, out var applied) && applied > turn)
				return;
			_applied[key] = turn;
			apply();
		}
	}
}