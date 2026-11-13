using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The size ladder that every tab chooses its layout from.</summary>
public sealed class LayoutTests
{
	[Theory]
	[InlineData(120, 32, "Wide")]
	[InlineData(119, 32, "Standard")]
	[InlineData(120, 31, "Standard")]
	[InlineData(80, 24, "Standard")]
	[InlineData(79, 24, "Narrow")]
	[InlineData(80, 23, "Narrow")]
	[InlineData(60, 16, "Narrow")]
	[InlineData(59, 16, "TooSmall")]
	[InlineData(60, 15, "TooSmall")]
	public void The_step_follows_the_size_of_the_terminal(int width, int height, string expected)
	{
		Assert.Equal(Enum.Parse<Breakpoint>(expected), Layout.Of(width, height, DashboardTab.Users));
	}

	[Fact]
	public void Chat_NeedsMoreRoomThanConsole()
	{
		var chat = Layout.Needed(DashboardTab.Chat);
		var console = Layout.Needed(DashboardTab.Console);

		Assert.True(chat.Width > console.Width && chat.Height > console.Height);
		Assert.Equal(Breakpoint.TooSmall, Layout.Of(60, 16, DashboardTab.Chat));
		Assert.NotEqual(Breakpoint.TooSmall, Layout.Of(60, 16, DashboardTab.Console));
	}

	[Fact]
	public void Control_characters_of_untrusted_text_never_reach_the_terminal()
	{
		Assert.Equal("a?[31mb\tc", Palette.Plain("a\u001b[31mb\tc"));
		Assert.Equal("plain", Palette.Plain("plain"));
	}

	[Fact]
	public void Palette_EscapesMarkupInText()
	{
		var wrapped = new Palette(true).Wrap(Ink.Text, "[red]x[/]");

		Assert.DoesNotContain("[red]x", wrapped.Replace("[[red]", string.Empty));
	}
}
