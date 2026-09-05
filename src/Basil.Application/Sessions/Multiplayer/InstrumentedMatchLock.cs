using System.Diagnostics;
using Basil.Application.Diagnostics;

namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     A drop-in replacement for the <see cref="SemaphoreSlim" /> backing <see cref="MatchSession.Lock" />,
///     exposing the exact same <see cref="WaitAsync(CancellationToken)" />/<see cref="Release" /> shape
///     so every existing call site keeps working unchanged, while recording how long each caller
///     actually waited to acquire it. Added for the 2026 performance investigation's ADR-003 —
///     answering whether match-lock contention (not just DB writes) contributes to the observed
///     latency, without touching the ~25 call sites across the handler/service/route layers.
/// </summary>
public sealed class InstrumentedMatchLock
{
	private readonly SemaphoreSlim _inner = new(1, 1);

	/// <summary>Waits to enter the lock, recording the wait duration once acquired.</summary>
	public async Task WaitAsync(CancellationToken cancellationToken = default)
	{
		var startedAt = Stopwatch.GetTimestamp();
		await _inner.WaitAsync(cancellationToken);
		BasilMetrics.MatchLockWaitMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
	}

	/// <summary>Releases the lock. See <see cref="SemaphoreSlim.Release()" />.</summary>
	public int Release()
	{
		return _inner.Release();
	}
}
