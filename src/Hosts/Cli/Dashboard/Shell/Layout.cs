using Basil.Host.Cli.Dashboard.Data;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>How much room the terminal gives the dashboard.</summary>
internal enum Breakpoint : byte
{
	/// <summary>Less than the tab in view needs.</summary>
	TooSmall,

	/// <summary>At least 60×16: one column, the secondary panes drill down.</summary>
	Narrow,

	/// <summary>At least 80×24.</summary>
	Standard,

	/// <summary>At least 120×32.</summary>
	Wide
}

/// <summary>The size ladder every tab chooses its layout from.</summary>
internal static class Layout
{
	/// <summary>The size of the narrow step.</summary>
	public static readonly (int Width, int Height) NarrowSize = (60, 16);

	/// <summary>The size of the standard step.</summary>
	public static readonly (int Width, int Height) StandardSize = (80, 24);

	/// <summary>The size of the wide step.</summary>
	public static readonly (int Width, int Height) WideSize = (120, 32);

	/// <summary>Picks the step for a terminal size.</summary>
	/// <param name="width">The columns.</param>
	/// <param name="height">The rows.</param>
	/// <param name="tab">The tab in view, which can need more than the narrow step.</param>
	public static Breakpoint Of(int width, int height, DashboardTab tab)
	{
		var (needWidth, needHeight) = Needed(tab);
		if (width < needWidth || height < needHeight) return Breakpoint.TooSmall;
		if (width >= WideSize.Width && height >= WideSize.Height) return Breakpoint.Wide;

		return width >= StandardSize.Width && height >= StandardSize.Height ? Breakpoint.Standard : Breakpoint.Narrow;
	}

	/// <summary>Gets the least size the tab can be drawn in.</summary>
	public static (int Width, int Height) Needed(DashboardTab tab)
	{
		return tab switch
		{
			DashboardTab.Console => (40, 10),
			DashboardTab.Chat => (70, 18),
			DashboardTab.Settings => (60, 18),
			_ => NarrowSize
		};
	}
}
