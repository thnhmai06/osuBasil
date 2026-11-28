using Basil.Host.Cli.Dashboard.Data;

namespace Basil.Host.Cli.Dashboard.Shell;

internal enum Breakpoint : byte
{
	TooSmall,

	Narrow,

	Standard,

	Wide
}

internal static class Layout
{
	public static readonly (int Width, int Height) NarrowSize = (60, 16);

	public static readonly (int Width, int Height) StandardSize = (80, 24);

	public static readonly (int Width, int Height) WideSize = (120, 32);

	public static Breakpoint Of(int width, int height, DashboardTab tab)
	{
		var (needWidth, needHeight) = Needed(tab);
		if (width < needWidth || height < needHeight) return Breakpoint.TooSmall;
		if (width >= WideSize.Width && height >= WideSize.Height) return Breakpoint.Wide;

		return width >= StandardSize.Width && height >= StandardSize.Height ? Breakpoint.Standard : Breakpoint.Narrow;
	}

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