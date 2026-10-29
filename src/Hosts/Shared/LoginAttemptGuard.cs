namespace Basil.Host.Shared;

/// <summary>
///     Limits failed sign-ins per address and per (address, account) pair: an attempt is reserved before the credentials
///     are checked, so concurrent guesses cannot all pass the limit together, and it stays counted only when the
///     credentials turned out to be wrong.
/// </summary>
public sealed class LoginAttemptGuard(RateLimiter limiter)
{
	/// <summary>How many times the address limit exceeds the limit of one (address, account) pair.</summary>
	public const int AddressFactor = 4;

	/// <summary>Reserves an attempt.</summary>
	/// <param name="ipKey">The key of the address.</param>
	/// <param name="pairKey">The key of the address and account pair.</param>
	/// <param name="maxAttempts">How many failed attempts one pair may make in the window.</param>
	/// <param name="window">The length of the window.</param>
	/// <returns>The reservation, or <see langword="null" /> when a limit is reached.</returns>
	public Attempt? TryReserve(string ipKey, string pairKey, int maxAttempts, TimeSpan window)
	{
		if (!limiter.TryAcquire(pairKey, maxAttempts, window)) return null;
		if (limiter.TryAcquire(ipKey, maxAttempts * AddressFactor, window)) return new Attempt(limiter, ipKey, pairKey);

		limiter.Refund(pairKey);
		return null;
	}

	/// <summary>A reserved attempt, given back on disposal unless the credentials were wrong.</summary>
	public sealed class Attempt : IDisposable
	{
		private readonly RateLimiter _limiter;
		private readonly string _ipKey;
		private readonly string _pairKey;
		private bool _kept;

		internal Attempt(RateLimiter limiter, string ipKey, string pairKey)
		{
			_limiter = limiter;
			_ipKey = ipKey;
			_pairKey = pairKey;
		}

		/// <summary>Keeps the attempt counted because the credentials were wrong.</summary>
		public void Keep()
		{
			_kept = true;
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (_kept) return;

			_kept = true;
			_limiter.Refund(_ipKey);
			_limiter.Refund(_pairKey);
		}
	}
}
