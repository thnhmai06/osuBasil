using System.Collections.Concurrent;

namespace Basil.Infrastructure.Beatmaps.Adapters;

/// <summary>
///     Lets the callers that ask for the same key at the same time share one run of the work for it.
/// </summary>
/// <typeparam name="TKey">The kind of key that tells two requests for the same work apart.</typeparam>
/// <typeparam name="TValue">The kind of value the work gives.</typeparam>
/// <remarks>
///     A caller that gives up waiting does not stop the work the others wait for. A run that fails is not kept: every
///     caller waiting for it gets its error, and the next call starts the work again.
/// </remarks>
internal sealed class SingleFlight<TKey, TValue> where TKey : notnull
{
	private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _flights = new();

	/// <summary>Gets the value of the work for a key, starting the work only when nobody is running it already.</summary>
	/// <param name="key">What the work is for.</param>
	/// <param name="work">The work; started on the thread pool by the first caller of the key only.</param>
	/// <param name="ct">A token that cancels this caller's wait, not the work.</param>
	/// <returns>The value the shared run gave.</returns>
	public Task<TValue> RunAsync(TKey key, Func<Task<TValue>> work, CancellationToken ct = default)
	{
		Lazy<Task<TValue>>? flight = null;
		flight = new Lazy<Task<TValue>>(() => Task.Run(async () =>
		{
			try
			{
				return await work();
			}
			finally
			{
				_flights.TryRemove(new KeyValuePair<TKey, Lazy<Task<TValue>>>(key, flight!));
			}
		}, CancellationToken.None));

		return _flights.GetOrAdd(key, flight).Value.WaitAsync(ct);
	}
}
