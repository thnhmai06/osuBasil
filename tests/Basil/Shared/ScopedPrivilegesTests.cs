using Basil.Application.Shared;

namespace Basil.Tests.Shared;

public class ScopedPrivilegesTests
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
	public void AllFlagsHeld_NotLacking()
	{
		var held = SampleBits.Read | SampleBits.Write;

		Assert.False(ScopedPrivileges.Lacks(held, SampleBits.Read));
		Assert.False(ScopedPrivileges.Lacks(held, SampleBits.Read | SampleBits.Write));
	}

	[Fact]
	public void MissingBit_Lacking()
	{
		var held = SampleBits.Read;

		Assert.True(ScopedPrivileges.Lacks(held, SampleBits.Read | SampleBits.Write | SampleBits.Delete));
	}

	[Fact]
	public void NoFlagsRequired_NotLacking()
	{
		var held = SampleBits.None;

		Assert.False(ScopedPrivileges.Lacks(held, SampleBits.None));
	}

	[Theory]
	[InlineData(SampleLevel.Admin, SampleLevel.Member, false)]
	[InlineData(SampleLevel.Member, SampleLevel.Member, false)]
	[InlineData(SampleLevel.Guest, SampleLevel.Guest, false)]
	[InlineData(SampleLevel.Member, SampleLevel.Admin, true)]
	[InlineData(SampleLevel.Guest, SampleLevel.Member, true)]
	public void RequiredLevel_LacksWhenBelow(SampleLevel held, SampleLevel required, bool lacks)
	{
		Assert.Equal(lacks, ScopedPrivileges.Lacks(held, required));
	}
}