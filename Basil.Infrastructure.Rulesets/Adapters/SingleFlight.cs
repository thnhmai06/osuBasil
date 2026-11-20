using System.Collections.Concurrent;

namespace Basil.Infrastructure.Rulesets.Adapters;

internal sealed class SingleFlight<TKey, TValue> where TKey : notnull
{
	private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _flights = new();

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