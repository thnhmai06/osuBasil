namespace Basil.Gateway.RateLimiting;

/// <summary>Counts failed sign-ins per account: attempts in flight count as failures until they end, so a burst cannot get past the limit.</summary>
public sealed class LoginAttemptGuard(TimeProvider clock)
{
	private static readonly TimeSpan Sweep = TimeSpan.FromMinutes(1);
	private readonly Dictionary<string, Counter> _accounts = new();
	private readonly Lock _lock = new();
	private DateTimeOffset _swept = clock.GetUtcNow();

	public int AccountCount
	{
		get
		{
			lock (_lock)
			{
				return _accounts.Count;
			}
		}
	}

	/// <summary>Reserves one attempt for an account; null when the account has used up its attempts for the window.</summary>
	public Attempt? TryReserve(string account, int maxAttempts, TimeSpan window)
	{
		var now = clock.GetUtcNow();
		lock (_lock)
		{
			SweepExpired(now);
			if (!_accounts.TryGetValue(account, out var counter) || now - counter.Since >= window)
			{
				counter = new Counter { Since = now };
				_accounts[account] = counter;
			}

			if (counter.Failed + counter.InFlight >= maxAttempts) return null;

			counter.InFlight++;
			counter.Window = window;
			return new Attempt(this, counter);
		}
	}

	// Runs inside a reservation, at most once a minute: a stored account costs a counter, so the table is bounded by the
	// accounts that failed lately.
	private void SweepExpired(DateTimeOffset now)
	{
		if (now - _swept < Sweep) return;

		_swept = now;
		foreach (var (account, counter) in _accounts.ToArray())
			if (counter.InFlight == 0 && now - counter.Since >= counter.Window)
				_accounts.Remove(account);
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
		private readonly Counter _counter;
		private readonly LoginAttemptGuard _guard;
		private bool _ended;

		internal Attempt(LoginAttemptGuard guard, Counter counter)
		{
			_guard = guard;
			_counter = counter;
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
				_counter.InFlight--;
				if (failed) _counter.Failed++;
			}
		}
	}
}
