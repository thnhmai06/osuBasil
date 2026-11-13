using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Graphs;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>
///     A rounded panel with a big current value on top, the graph of one or two series that fills the rest and a legend
///     line; the graph is drawn again for the size the panel has now.
/// </summary>
internal sealed class GraphPanel
{
	private static readonly int[,] DotBits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

	private readonly Func<double> _ceiling;
	private readonly State<int> _version = new(0);
	private readonly Glyphs _glyphs;
	private readonly bool _noColor;
	private readonly Series _primary;
	private readonly Series? _secondary;
	private readonly Func<Theme> _theme;

	/// <summary>Creates the panel.</summary>
	/// <param name="title">The title in the border.</param>
	/// <param name="primary">The series drawn as a filled area.</param>
	/// <param name="secondary">The series drawn as a dotted line over it, or <see langword="null" />.</param>
	/// <param name="ceiling">The value that reaches the top of the graph.</param>
	/// <param name="headline">The big current value, as markup.</param>
	/// <param name="legend">The legend line, as markup; empty for none.</param>
	/// <param name="theme">The theme that colours the graph.</param>
	/// <param name="glyphs">Tells whether the graph stays in ASCII.</param>
	/// <param name="noColor">Whether the graph is monochrome.</param>
	/// <param name="framed">Whether the panel has its own border; without it the panel goes inside another frame.</param>
	public GraphPanel(string title, Series primary, Series? secondary, Func<double> ceiling, Func<string> headline,
		string legend, Func<Theme> theme, Glyphs glyphs, bool noColor, bool framed = true)
	{
		_primary = primary;
		_secondary = secondary;
		_ceiling = ceiling;
		_theme = theme;
		_glyphs = glyphs;
		_noColor = noColor;
		var canvas = new Canvas().Painter(() =>
		{
			_ = _version.Value;
			return new Delegator<Action<CanvasContext>>(Paint);
		}).Stretch();
		var body = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Star() },
				new RowDefinition { Height = GridLength.Auto })
			.Cell(new Markup(() =>
			{
				_ = _version.Value;
				return headline();
			}).HorizontalAlignment(Align.End), 0, 0)
			.Cell(canvas, 1, 0)
			.Cell(new Markup(legend), 2, 0)
			.Stretch();
		View = !framed ? (Visual)body : new Group(title).Style(glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded)
			.Padding(new Thickness(1, 0, 1, 0)).Content(body).Stretch();
	}

	/// <summary>Gets the panel.</summary>
	public Visual View { get; }

	/// <summary>Gets the legend marker of the primary series.</summary>
	public static string PrimaryMark(Glyphs glyphs)
	{
		return glyphs.Ascii ? "|" : "━";
	}

	/// <summary>Gets the legend marker of the secondary series.</summary>
	public static string SecondaryMark(Glyphs glyphs)
	{
		return glyphs.Ascii ? "." : "┄";
	}

	/// <summary>Draws the graph again, after the series got a sample.</summary>
	public void Refresh()
	{
		_version.Value++;
	}

	private void Paint(CanvasContext context)
	{
		IPaintSurface surface = new CanvasSurface(context, _theme());
		if (_noColor) surface = new PlainSurface(surface);

		var area = new Area(0, 0, context.Size.Width, context.Size.Height);
		var top = Math.Max(_ceiling(), 1e-9);
		var secondary = _secondary?.Values();
		if (_glyphs.Ascii)
		{
			AsciiBars(surface, area, _primary.Values(), top);
			if (secondary is not null) AsciiLine(surface, area, secondary, top);
		}
		else
		{
			BrailleGraph.Draw(surface, area, _primary.Values(), top);
			if (secondary is not null) BrailleLine(surface, area, secondary, top);
		}
	}

	private static void AsciiBars(IPaintSurface surface, Area area, double[] values, double top)
	{
		var shown = Tail(values, area.Width);
		for (var i = 0; i < shown.Length; i++)
		{
			var x = area.X + area.Width - shown.Length + i;
			var level = Math.Clamp(shown[i] / top, 0, 1);
			var height = Math.Max(1, (int)Math.Round(level * area.Height));
			for (var row = 0; row < height; row++)
				surface.Put(x, area.Y + area.Height - 1 - row, row == height - 1 ? ':' : '|', Tone.Heat, level);
		}
	}

	private static void AsciiLine(IPaintSurface surface, Area area, double[] values, double top)
	{
		var shown = Tail(values, area.Width);
		for (var i = 0; i < shown.Length; i++)
		{
			var level = Math.Clamp(shown[i] / top, 0, 1);
			var row = area.Height - 1 - (int)Math.Round(level * (area.Height - 1));
			surface.Put(area.X + area.Width - shown.Length + i, area.Y + row, '.', Tone.Accent);
		}
	}

	private static void BrailleLine(IPaintSurface surface, Area area, double[] values, double top)
	{
		if (area.Width <= 0 || area.Height <= 0) return;

		var shown = Tail(values, area.Width * 2);
		var rows = area.Height * 4;
		var cells = new int[area.Width, area.Height];
		var offset = area.Width * 2 - shown.Length;
		for (var i = 0; i < shown.Length; i++)
		{
			var dotX = offset + i;
			var y = rows - 1 - (int)Math.Round(Math.Clamp(shown[i] / top, 0, 1) * (rows - 1));
			cells[dotX / 2, y / 4] |= DotBits[dotX % 2, y % 4];
		}

		for (var x = 0; x < area.Width; x++)
		for (var y = 0; y < area.Height; y++)
			if (cells[x, y] != 0) surface.Put(area.X + x, area.Y + y, (char)(0x2800 + cells[x, y]), Tone.Accent);
	}

	private static double[] Tail(double[] values, int count)
	{
		return values.Length > count ? values[^count..] : values;
	}

	/// <summary>Draws every tone as ordinary text, for the monochrome dashboard.</summary>
	private sealed class PlainSurface(IPaintSurface inner) : IPaintSurface
	{
		public int Width => inner.Width;

		public int Height => inner.Height;

		public void Put(int x, int y, char character, Tone tone, double heat = 0)
		{
			inner.Put(x, y, character, Tone.Text, heat);
		}
	}
}
