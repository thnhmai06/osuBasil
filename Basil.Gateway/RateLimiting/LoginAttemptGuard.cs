using System.Net;
using System.Net.Sockets;

namespace Basil.Gateway.RateLimiting;

/// <summary>
///     Counts failed sign-ins per address and account, and in total per address: attempts in flight count as failures
///     until they end, so a burst cannot get past the limit.
/// </summary>
/// <remarks>
///     The pair is what is limited, so failures from one address never lock the account out for its owner elsewhere;
///     the total per address stops one address from guessing across many accounts.
/// </remarks>
public sealed class LoginAttemptGuard(TimeProvider clock)
{
	/// <summary>How many times the pair limit one address may fail in total across accounts.</summary>
	public const int AddressFactor = 10;

	private static readonly TimeSpan Sweep = TimeSpan.FromMinutes(1);
	private readonly Dictionary<string, Counter> _counters = new();
	private readonly Lock _lock = new();
	private DateTimeOffset _swept = clock.GetUtcNow();

	public int AccountCount
	{
		get
		{
			lock (_lock)
			{
				return _counters.Count;
			}
		}
	}

	/// <summary>
	///     Reserves one attempt for an account from an address; null when the pair, or the address in total, has used up
	///     its attempts for the window.
	/// </summary>
	public Attempt? TryReserve(IPAddress address, string account, int maxAttempts, TimeSpan window)
	{
		var now = clock.GetUtcNow();
		lock (_lock)
		{
			SweepExpired(now);
			var source = SourceOf(address);
			var pair = Current($"{source}|{account}", window, now);
			var total = Current(source, window, now);
			if (pair.Failed + pair.InFlight >= maxAttempts ||
			    total.Failed + total.InFlight >= maxAttempts * AddressFactor)
				return null;

			pair.InFlight++;
			total.InFlight++;
			return new Attempt(this, pair, total);
		}
	}

	// One IPv6 subscriber holds a whole /64, so the /64 is one source; an IPv4-mapped address is its IPv4 address.
	private static string SourceOf(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6) return address.MapToIPv4().ToString();
		if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

		var bytes = address.GetAddressBytes();
		Array.Clear(bytes, 8, 8);
		return $"{new IPAddress(bytes)}/64";
	}

	private Counter Current(string key, TimeSpan window, DateTimeOffset now)
	{
		if (!_counters.TryGetValue(key, out var counter) || now - counter.Since >= window)
		{
			counter = new Counter { Since = now };
			_counters[key] = counter;
		}

		counter.Window = window;
		return counter;
	}

	// Runs inside a reservation, at most once a minute: a stored key costs a counter, so the table is bounded by the
	// addresses and accounts that failed lately.
	private void SweepExpired(DateTimeOffset now)
	{
		if (now - _swept < Sweep) return;

		_swept = now;
		foreach (var (key, counter) in _counters.ToArray())
			if (counter.InFlight == 0 && now - counter.Since >= counter.Window)
				_counters.Remove(key);
	}

	internal sealed class Counter
	{
		public int Failed;
		public int InFlight;
		public DateTimeOffset Since;
		public TimeSpan Window;
	}

	public sealed class Attempt : IDisposable
	{
		private readonly Counter _pair;
		private readonly Counter _total;
		private readonly LoginAttemptGuard _guard;
		private bool _ended;

		internal Attempt(LoginAttemptGuard guard, Counter pair, Counter total)
		{
			_guard = guard;
			_pair = pair;
			_total = total;
		}

		/// <summary>Ends the attempt without a failure.</summary>
		public void Dispose()
		{
			End(false);
		}

		/// <summary>Counts the attempt as a failure.</summary>
		public void Keep()
		{
			End(true);
		}

		private void End(bool failed)
		{
			lock (_guard._lock)
			{
				if (_ended) return;

				_ended = true;
				foreach (var counter in new[] { _pair, _total })
				{
					counter.InFlight--;
					if (failed) counter.Failed++;
				}
			}
		}
	}
}