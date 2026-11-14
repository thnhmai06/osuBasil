using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Tests.Dashboard;

public sealed class InkTests
{
	private static readonly Ink[] Basics = [Ink.Text, Ink.Muted, Ink.Accent, Ink.Success, Ink.Warning, Ink.Error, Ink.Info];

	[Fact]
	public void EveryKind_IsShownAsABasicKind()
	{
		foreach (var ink in Enum.GetValues<Ink>()) Assert.Contains(InkTable.Basic(ink), Basics);
	}

	[Theory]
	[InlineData("RoleRestricted", "Error")]
	[InlineData("GradeD", "Error")]
	[InlineData("RoomClosed", "Muted")]
	[InlineData("MapRanked", "Success")]
	public void ExpectedKinds_AreShownAsTheirMeaning(string named, string basic)
	{
		Assert.Equal(basic, InkTable.Basic(Enum.Parse<Ink>(named)).ToString());
	}

	[Fact]
	public void NoColor_StillWrapsNamedKindsWithoutColourTags()
	{
		Assert.DoesNotContain("red", new Palette(true).Wrap(Ink.RoleRestricted, "x"));
	}
}
