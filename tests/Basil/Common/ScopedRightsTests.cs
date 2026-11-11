using Basil.Application.Common;

namespace Basil.Tests.Common;

public class ScopedRightsTests
{
	[Flags]
	public enum SampleBits
	{
		None = 0,
		Read = 1,
		Write = 2,
		Delete = 4
	}

	public enum SampleLevel
	{
		Guest = 0,
		Member = 1,
		Admin = 2
	}

	[Fact]
	public void AllFlagsHeld_Met()
	{
		var held = SampleBits.Read | SampleBits.Write;

		Assert.Null(ScopedRights.Require(held, SampleBits.Read));
		Assert.Null(ScopedRights.Require(held, SampleBits.Read | SampleBits.Write));
	}

	[Fact]
	public void MissingBit_OnlyMissingNamed()
	{
		var held = SampleBits.Read;

		Assert.Equal(SampleBits.Write | SampleBits.Delete,
			ScopedRights.Require(held, SampleBits.Read | SampleBits.Write | SampleBits.Delete));
	}

	[Fact]
	public void NoFlagsRequired_Met()
	{
		var held = SampleBits.None;

		Assert.Null(ScopedRights.Require(held, SampleBits.None));
	}

	[Theory]
	[InlineData(SampleLevel.Admin, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Guest, SampleLevel.Guest, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Admin, false)]
	[InlineData(SampleLevel.Guest, SampleLevel.Member, false)]
	public void RequiredLevelHeld_Met(SampleLevel held, SampleLevel required,
		bool met)
	{
		var missing = ScopedRights.Require(held, required);

		Assert.Equal(met ? null : required, missing);
	}
}