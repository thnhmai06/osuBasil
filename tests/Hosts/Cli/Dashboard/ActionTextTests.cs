using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Tabs;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The text the buttons of the detail frame hand to the console and to confirmation dialogs.</summary>
public sealed class ActionTextTests
{
	[Theory]
	[InlineData("plain", "\"plain\"")]
	[InlineData("two words", "\"two words\"")]
	[InlineData("", "\"\"")]
	public void Quote_MakesOneWordOfAnyValue(string value, string expected)
	{
		Assert.Equal(expected, CommandActions.Quote(value));
	}

	[Theory]
	[InlineData("a \"quoted\" reason")]
	[InlineData("back\\slash")]
	[InlineData("-looks-like-text")]
	public void Quote_SurvivesTheSplittingOfACommandLine(string value)
	{
		var words = CommandCatalog.Split($"cmd --reason {CommandActions.Quote(value)}");

		Assert.Equal(["cmd", "--reason", value], words);
	}

	[Fact]
	public void WrapText_KeepsEveryLineWithinTheWidthAndEveryWord()
	{
		var lines = Dialogs.WrapText("Close session 5 of bob? The client is disconnected and the token stops working.", 20);

		Assert.All(lines, line => Assert.True(line.Length <= 20));
		Assert.Equal("Close session 5 of bob? The client is disconnected and the token stops working.", string.Join(' ', lines));
	}

	[Fact]
	public void WrapText_OfNothing_IsNoLines()
	{
		Assert.Empty(Dialogs.WrapText(string.Empty, 20));
	}
}
