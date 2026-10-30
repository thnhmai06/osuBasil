using Basil.Domain.Client;

namespace Basil.Domain.Tests.Bancho;

/// <summary>Verifies how anticheat flags are read as readable names.</summary>
public class OsuFlagsExtensionsTests
{
	[Fact]
	public void SignNames_Clean_IsEmpty()
	{
		Assert.Empty(OsuFlags.Clean.SignNames());
	}

	[Fact]
	public void SignNames_SingleFlag_IsItsName()
	{
		Assert.Equal(["SpeedHackDetected"], OsuFlags.SpeedHackDetected.SignNames());
	}

	[Fact]
	public void SignNames_SeveralFlags_AreListedInAscendingBitOrder()
	{
		var flags = OsuFlags.AqnMenuSample | OsuFlags.FastPress | OsuFlags.IncorrectModValue;

		Assert.Equal(["IncorrectModValue", "FastPress", "AqnMenuSample"], flags.SignNames());
	}

	[Fact]
	public void SignNames_UndefinedBit_IsIgnored()
	{
		Assert.Equal(["ChecksumFailure"], (OsuFlags.ChecksumFailure | (OsuFlags)(1u << 30)).SignNames());
	}

	[Fact]
	public void SignNames_EveryDefinedFlag_ListsOneNameEach()
	{
		var all = Enum.GetValues<OsuFlags>().Aggregate(OsuFlags.Clean, (a, b) => a | b);

		Assert.Equal(Enum.GetValues<OsuFlags>().Length - 1, all.SignNames().Count);
	}
}