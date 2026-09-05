using Basil.Application.Services;
using Xunit;

namespace Basil.Application.Tests.Services;

public class SequenceGateTests
{
	[Fact]
	public void TryAdvance_StrictlyIncreasing_AllAccepted()
	{
		var gate = new SequenceGate();

		Assert.True(gate.TryAdvance(1));
		Assert.True(gate.TryAdvance(2));
		Assert.True(gate.TryAdvance(10));
	}

	[Fact]
	public void TryAdvance_SameOrOlderSequence_Rejected()
	{
		var gate = new SequenceGate();
		gate.TryAdvance(5);

		Assert.False(gate.TryAdvance(5));
		Assert.False(gate.TryAdvance(4));
	}

	[Fact]
	public void TryAdvance_OutOfOrderArrival_OnlyTheNewestSequenceWins()
	{
		// Models the exact race this gate exists to resolve: two unlocked callers computed
		// sequences 5 and 6 while the match lock was held (in true mutation order), but the older
		// one (5) happens to finish its unlocked work and arrive second.
		var gate = new SequenceGate();

		Assert.True(gate.TryAdvance(6));
		Assert.False(gate.TryAdvance(5));
	}

	/// <summary>
	///     Many threads racing to advance the SAME sequence number -- proves the compare-and-swap
	///     itself is race-free, with no double-accept under real concurrency.
	/// </summary>
	[Fact]
	public void TryAdvance_ManyThreadsSameSequence_ExactlyOneWins()
	{
		var gate = new SequenceGate();
		var winners = 0;

		Parallel.For(0, 200, _ =>
		{
			if (gate.TryAdvance(5)) Interlocked.Increment(ref winners);
		});

		Assert.Equal(1, winners);
	}
}