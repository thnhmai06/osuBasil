namespace Basil.Gateway.RateLimiting;

public sealed class LoginAttemptGuard(RateLimiter limiter)
{
	public const int AddressFactor = 4;

	public Attempt? TryReserve(string ipKey, string pairKey, int maxAttempts, TimeSpan window)
	{
		if (!limiter.TryAcquire(pairKey, maxAttempts, window)) return null;
		if (limiter.TryAcquire(ipKey, maxAttempts * AddressFactor, window)) return new Attempt(limiter, ipKey, pairKey);

		limiter.Refund(pairKey);
		return null;
	}

	public sealed class Attempt : IDisposable
	{
		private readonly string _ipKey;
		private readonly RateLimiter _limiter;
		private readonly string _pairKey;
		private bool _kept;

		internal Attempt(RateLimiter limiter, string ipKey, string pairKey)
		{
			_limiter = limiter;
			_ipKey = ipKey;
			_pairKey = pairKey;
		}

		public void Dispose()
		{
			if (_kept) return;

			_kept = true;
			_limiter.Refund(_ipKey);
			_limiter.Refund(_pairKey);
		}

		public void Keep()
		{
			_kept = true;
		}
	}
}