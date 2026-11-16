using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
using Microsoft.Extensions.Time.Testing;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The toasts: the same message on screen is counted, not shown twice, and no more than four show at once.</summary>
public sealed class ToastTests
{
	private static (ToastTray Tray, Func<List<string>> Shown) Create()
	{
		var tray = new ToastTray(new Glyphs(false), new Palette(false), new FakeTimeProvider(), action => action());
		var page = tray.Over(new TextBlock("page"));
		// The toasts on screen: the slots that are visible, with the text each one shows.
		List<string> Shown() => [.. page.EnumerateVisualsDepthFirst().OfType<Group>().Where(group => group.IsVisible)
			.Select(group => group.EnumerateVisualsDepthFirst().OfType<Markup>().First().Text ?? string.Empty)];
		return (tray, Shown);
	}

	[Fact]
	public void SameText_WhileOnScreen_IsOneToastCountedTwice()
	{
		var (tray, shown) = Create();

		tray.Show("Saved");
		tray.Show("Saved");

		var toast = Assert.Single(shown());
		Assert.Contains("×2", toast);
	}

	[Fact]
	public void DifferentText_IsTwoToasts()
	{
		var (tray, shown) = Create();

		tray.Show("Saved");
		tray.Show("Failed");

		Assert.Equal(2, shown().Count);
	}

	[Fact]
	public void OneToast_HasNoCount()
	{
		var (tray, shown) = Create();

		tray.Show("Saved");

		Assert.DoesNotContain("×", Assert.Single(shown()));
	}

	[Fact]
	public void MoreThanFourDifferentMessages_ShowFour()
	{
		var (tray, shown) = Create();

		for (var i = 0; i < 6; i++) tray.Show($"Message {i}");

		Assert.Equal(ToastTray.MaxVisible, shown().Count);
		Assert.Equal(4, ToastTray.MaxVisible);
	}
}
