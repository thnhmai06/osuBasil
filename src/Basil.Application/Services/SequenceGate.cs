namespace Basil.Application.Services;

/// <summary>
///     Accepts only strictly increasing sequence numbers, guarding against an out-of-order call
///     reapplying stale state after the lock that established the true order has been released.
/// </summary>
/// <remarks>
///     A caller allocates a sequence number while still holding whatever lock serializes its own
///     state mutation (see <see cref="Sessions.Multiplayer.MatchSession.NextStateVersion" />) — at
///     the moment the mutation completes, before releasing the lock — then does its
///     build-and-broadcast work unlocked, passing that sequence to <see cref="TryAdvance" />. Two
///     unlocked calls can race and complete in either order; whichever carries the higher sequence
///     is the one that must win, regardless of which happened to finish last. A call whose sequence
///     has already been superseded is a normal, benign outcome of that race, not an error — the
///     caller must skip whatever it was about to apply.
/// </remarks>
public sealed class SequenceGate
{
	private long _lastApplied = -1;

	/// <summary>Records <paramref name="sequence" /> as applied if it is newer than the last one accepted.</summary>
	/// <param name="sequence">The sequence number to attempt to apply.</param>
	/// <returns><see langword="true" /> if <paramref name="sequence" /> was newer and is now recorded; otherwise, <see langword="false" />.</returns>
	public bool TryAdvance(long sequence)
	{
		while (true)
		{
			var last = Volatile.Read(ref _lastApplied);
			if (sequence <= last) return false;
			if (Interlocked.CompareExchange(ref _lastApplied, sequence, last) == last) return true;
		}
	}
}
