namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     Arbitrates a scenario copy's held resource between its owner and the sweeps of other copies, so the owner
///     never runs on a resource that was reaped and exactly one party tears it down.
/// </summary>
internal sealed class IdleGate
{
	private const int Free = 0;
	private const int Busy = 1;
	private const int Reaped = 2;
	private long _lastExit = Environment.TickCount64;

	// A gate starts busy: its owner creates it in the middle of an invocation.
	private int _state = Busy;

	/// <summary>Claims the resource for an invocation; returns <see langword="false" /> when it was reaped.</summary>
	public bool TryEnter()
	{
		return Interlocked.CompareExchange(ref _state, Busy, Free) == Free;
	}

	/// <summary>Releases the resource after an invocation and restarts its idle time.</summary>
	public void Exit()
	{
		Volatile.Write(ref _lastExit, Environment.TickCount64);
		Volatile.Write(ref _state, Free);
	}

	/// <summary>
	///     Claims the resource for teardown when no invocation is using it and it has been idle for at least
	///     <paramref name="idle" />.
	/// </summary>
	public bool TryReap(TimeSpan idle)
	{
		if (IsFresh(idle) || Interlocked.CompareExchange(ref _state, Reaped, Free) != Free) return false;
		if (!IsFresh(idle)) return true;

		Volatile.Write(ref _state, Free);
		return false;
	}

	private bool IsFresh(TimeSpan idle)
	{
		return Environment.TickCount64 - Volatile.Read(ref _lastExit) < idle.TotalMilliseconds;
	}
}