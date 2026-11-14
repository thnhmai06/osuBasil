using Basil.Domain.Client;

namespace Basil.Domain.Tests.Stable;

/// <summary>Verifies how anticheat flags are read as readable names.</summary>
public class OsuFlagsExtensionsTests
{
	[Fact]
	public void Clean_NoNames()
	{
		Assert.Empty(OsuFlags.Clean.SignNames());
	}

	[Fact]
	public void SingleFlag_ItsName()
	{
		Assert.Equal(["SpeedHackDetected"], OsuFlags.SpeedHackDetected.SignNames());
	}

	[Fact]
	public void SeveralFlags_AscendingBitOrder()
	{
		var flags = OsuFlags.AqnMenuSample | OsuFlags.FastPress | OsuFlags.IncorrectModValue;

		Assert.Equal(["IncorrectModValue", "FastPress", "AqnMenuSample"], flags.SignNames());
	}

	[Fact]
	public void UndefinedBit_Ignored()
	{
		Assert.Equal(["ChecksumFailure"], (OsuFlags.ChecksumFailure | (OsuFlags)(1u << 30)).SignNames());
	}

	[Fact]
	public void EveryDefinedFlag_OneNameEach()
	{
		var all = Enum.GetValues<OsuFlags>().Aggregate(OsuFlags.Clean, (a, b) => a | b);

		Assert.Equal(Enum.GetValues<OsuFlags>().Length - 1, all.SignNames().Count);
	}
}