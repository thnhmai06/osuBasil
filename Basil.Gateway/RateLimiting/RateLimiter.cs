using System.Globalization;
using System.Threading.RateLimiting;

namespace Basil.Gateway.RateLimiting;

/// <summary>The rate rules that hosts apply by name; the gateway owns how each one is keyed.</summary>
public enum RateRule : byte
{
	/// <summary>Chat messages a user sends.</summary>
	Chat,

	/// <summary>Changes a user makes through the HTTP API.</summary>
	RestMutation,

	/// <summary>Accounts registered through the game client, for the whole server.</summary>
	Registration,

	/// <summary>Forced garbage collections the diagnostics API starts, for the whole server.</summary>
	GcCollect
}

/// <summary>Counts events per key in a sliding window; a key costs the same however many events it saw.</summary>
public sealed class RateLimiter : IDisposable
{
	private const int Segments = 6;

	private readonly PartitionedRateLimiter<Request> _limiter = PartitionedRateLimiter.Create<Request, string>(
		request => RateLimitPartition.GetSlidingWindowLimiter(request.PartitionKey,
			_ => new SlidingWindowRateLimiterOptions
			{
				PermitLimit = request.Limit,
				Window = request.Window,
				SegmentsPerWindow = Segments,
				QueueLimit = 0,
				AutoReplenishment = true
			}), StringComparer.Ordinal);

	public void Dispose()
	{
		_limiter.Dispose();
	}

	/// <summary>Takes one event for a rule and subject; false when the subject is over the count for the window.</summary>
	public bool TryAcquire(RateRule rule, string subject, int count, TimeSpan window)
	{
		return TryAcquire($"{rule}:{subject}", count, window);
	}

	/// <summary>Takes <paramref name="permits"/> events for a key; false when that would pass the count for the window.</summary>
	public bool TryAcquire(string key, int count, TimeSpan window, int permits = 1)
	{
		if (permits > count) return false;

		// A changed limit or window is a new partition; the old one is dropped once it goes idle.
		var partition = string.Create(CultureInfo.InvariantCulture, $"{key}|{count}|{window.Ticks}");
		using var lease = _limiter.AttemptAcquire(new Request(partition, count, window), Math.Min(permits, count));
		return lease.IsAcquired && permits <= count;
	}

	private readonly record struct Request(string PartitionKey, int Limit, TimeSpan Window);
}
