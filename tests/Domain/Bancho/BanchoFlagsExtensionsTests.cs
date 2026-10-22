using Basil.Domain.Bancho;

namespace Basil.Domain.Tests.Bancho;

/// <summary>Verifies how anticheat flags are read as readable names.</summary>
public class BanchoFlagsExtensionsTests
{
	[Fact]
	public void SignNames_Clean_IsEmpty()
	{
		Assert.Empty(BanchoFlags.Clean.SignNames());
	}

	[Fact]
	public void SignNames_SingleFlag_IsItsName()
	{
		Assert.Equal(["SpeedHackDetected"], BanchoFlags.SpeedHackDetected.SignNames());
	}

	[Fact]
	public void SignNames_SeveralFlags_AreListedInAscendingBitOrder()
	{
		var flags = BanchoFlags.AqnMenuSample | BanchoFlags.FastPress | BanchoFlags.IncorrectModValue;

		Assert.Equal(["IncorrectModValue", "FastPress", "AqnMenuSample"], flags.SignNames());
	}

	[Fact]
	public void SignNames_UndefinedBit_IsIgnored()
	{
		Assert.Equal(["ChecksumFailure"], (BanchoFlags.ChecksumFailure | (BanchoFlags)(1u << 30)).SignNames());
	}

	[Fact]
	public void SignNames_EveryDefinedFlag_ListsOneNameEach()
	{
		var all = Enum.GetValues<BanchoFlags>().Aggregate(BanchoFlags.Clean, (a, b) => a | b);

		Assert.Equal(Enum.GetValues<BanchoFlags>().Length - 1, all.SignNames().Count);
	}
}
