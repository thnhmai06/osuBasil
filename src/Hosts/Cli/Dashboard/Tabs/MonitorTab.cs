using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The machine and the server over the last fifteen minutes: CPU, memory, network and connections.</summary>
internal sealed class MonitorTab : ITabView
{
	private const int WarningPercent = 70;
	private const int ErrorPercent = 90;
	private const int MeterWidth = 20;
	private readonly Glyphs _glyphs;
	private readonly DashboardModel _model;
	private readonly Palette _palette;
	private readonly List<GraphPanel> _panels = [];
	private readonly Func<(int Columns, int Rows)> _size;
	private readonly Sparkline _connectionsLine = new();
	private readonly Sparkline _networkLine = new();
	private readonly State<int> _stamp = new(0);
	private object? _seen;

	/// <summary>Creates the tab.</summary>
	/// <param name="model">What the dashboard sampled.</param>
	/// <param name="themes">The theme that colours the graphs.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="options">Tells whether the dashboard is monochrome.</param>
	/// <param name="size">The size of the terminal in columns and rows; it must read the state that changes on resize.</param>
	public MonitorTab(DashboardModel model, ThemeStore themes, Glyphs glyphs, CliOptions options,
		Func<(int Columns, int Rows)> size)
	{
		_model = model;
		_glyphs = glyphs;
		_size = size;
		_palette = new Palette(options.NoColor);
		Func<Theme> theme = () => themes.Theme;
		var cpu = Panel("CPU", model.MachineCpu, model.Cpu, () => 100, () => $"{Percent(model.MachineCpu.Latest)} / {Percent(model.Cpu.Latest)}",
			Legend("Machine", "Process"), theme, options);
		var ram = Panel("RAM", model.MachineMemory, model.ProcessMemory, Total, RamHeadline,
			Legend("Used", "Process"), theme, options);
		var network = Panel("Network", model.NetworkReceived, model.NetworkSent,
			() => Math.Max(Math.Max(model.NetworkReceived.Max(), model.NetworkSent.Max()) * 1.1, 0.1), NetworkHeadline,
			Legend("Receive", "Send"), theme, options);
		var open = Panel("Connections", model.ConnectionsOpen, null, () => Math.Max(model.ConnectionsOpen.Max() * 1.1, 1),
			() => _palette.Wrap(Ink.Accent, model.ConnectionsOpen.Latest.ToString("0")), Legend("Open", null), theme,
			options);

		var wide = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() }, new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Star() })
			.Cell(cpu.View, 0, 0).Cell(ram.View, 0, 1).Cell(network.View, 1, 0).Cell(open.View, 1, 1).Stretch();
		var categories = new ScrollViewer(new ComputedVisual(Categories)).MaxHeight(9);
		var wideBody = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(wide, 0, 0)
			.Cell(categories.IsVisible(() => _size().Rows >= Layout.WideSize.Height), 1, 0)
			.Stretch().IsVisible(() => !IsNarrow());

		var narrow = new VStack(
			new Markup(() => Meter("CPU", model.MachineCpu.Latest, model.Cpu.Latest)),
			new Markup(() => Meter("RAM", Fraction(model.MachineMemory.Latest, Total()) * 100,
				Fraction(model.ProcessMemory.Latest, Total()) * 100)),
			new HStack(new Markup(_palette.Wrap(Ink.Muted, "Net  ")), _networkLine.Stretch(),
				new Markup(() => " " + Stamped(NetworkHeadline))),
			new HStack(new Markup(_palette.Wrap(Ink.Muted, "Conn ")), _connectionsLine.Stretch(),
				new Markup(() => " " + Stamped(() => _palette.Wrap(Ink.Accent, model.ConnectionsOpen.Latest.ToString("0"))))),
			new ScrollViewer(new ComputedVisual(Categories)).Stretch()).Spacing(0).IsVisible(IsNarrow);
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(wideBody, 0, 0)
			.Cell(narrow, 0, 0);
	}

	/// <inheritdoc />
	public Visual View { get; }

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
	}

	/// <inheritdoc />
	public void Activate()
	{
		Update();
	}

	/// <inheritdoc />
	public void Tick()
	{
		if (!ReferenceEquals(_seen, _model.Diagnostics)) Update();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Update();
	}

	private void Update()
	{
		_seen = _model.Diagnostics;
		foreach (var panel in _panels) panel.Refresh();
		_networkLine.Values(_model.NetworkReceived.Values());
		_connectionsLine.Values(_model.ConnectionsOpen.Values());
		_stamp.Value++;
	}

	private GraphPanel Panel(string title, Series primary, Series? secondary, Func<double> ceiling,
		Func<string> headline, string legend, Func<Theme> theme, CliOptions options)
	{
		var panel = new GraphPanel(title, primary, secondary, ceiling, headline, legend, theme, _glyphs, options.NoColor);
		_panels.Add(panel);
		return panel;
	}

	private bool IsNarrow()
	{
		var (columns, rows) = _size();
		return Layout.Of(columns, rows, DashboardTab.Monitor) is Breakpoint.Narrow or Breakpoint.TooSmall;
	}

	private string Legend(string primary, string? secondary)
	{
		var first = $"{_palette.Wrap(Ink.Accent, GraphPanel.PrimaryMark(_glyphs))} {primary}";
		return secondary is null
			? first
			: $"{first}   {_palette.Wrap(Ink.Accent, GraphPanel.SecondaryMark(_glyphs))} {secondary}";
	}

	private string Stamped(Func<string> text)
	{
		_ = _stamp.Value;
		return text();
	}

	private double Total()
	{
		return _model.MachineMemoryTotal > 0 ? _model.MachineMemoryTotal : Math.Max(_model.MachineMemory.Max(), 1);
	}

	private static double Fraction(double value, double total)
	{
		return total <= 0 ? 0 : Math.Clamp(value / total, 0, 1);
	}

	private static Ink Threshold(double percent)
	{
		return percent >= ErrorPercent ? Ink.Error : percent >= WarningPercent ? Ink.Warning : Ink.Success;
	}

	private string Percent(double value)
	{
		return _palette.Wrap(Threshold(value), $"{value:0} %");
	}

	private string RamHeadline()
	{
		var used = _model.MachineMemory.Latest;
		var colour = Threshold(Fraction(used, Total()) * 100);
		return $"{_palette.Wrap(colour, $"{used:0.0}")} / {_model.ProcessMemory.Latest:0.0} / {Total():0} GB";
	}

	private string NetworkHeadline()
	{
		var down = _glyphs.Ascii ? "v" : "↓";
		var up = _glyphs.Ascii ? "^" : "↑";
		return $"{down} {_model.NetworkReceived.Latest:0.0}   {up} {_model.NetworkSent.Latest:0.0}  MB/s";
	}

	private string Meter(string label, double machine, double process)
	{
		_ = _stamp.Value;
		var filled = (int)Math.Round(Math.Clamp(machine, 0, 100) / 100 * MeterWidth);
		var (full, empty, left, right) = _glyphs.Ascii ? ('#', '-', '[', ']') : ('█', '░', '▕', '▏');
		var bar = new string(full, filled) + new string(empty, MeterWidth - filled);
		return $"{_palette.Wrap(Ink.Muted, label.PadRight(4))}{left}{_palette.Wrap(Threshold(machine), bar)}{right} " +
		       $"{Percent(machine)} / {Percent(process)}";
	}

	private Visual Categories()
	{
		_ = _stamp.Value;
		var groups = _model.Diagnostics.GroupBy(line => line.Name.Split('.')[0]);
		var style = _glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded;
		var panels = groups.Select(group =>
		{
			var lines = group.Select(line => (Name: line.Name[(group.Key.Length + 1)..], line.Value)).ToList();
			if (lines.Count == 0) lines.Add((string.Empty, "-"));
			var width = lines.Max(line => line.Name.Length);
			var text = string.Join('\n', lines.Select(line =>
				$"{_palette.Wrap(Ink.Muted, line.Name.PadRight(width))} : {_palette.Wrap(Ink.Text, line.Value)}"));
			return (Visual)new Group(group.Key).Style(style).Padding(new Thickness(1, 0, 1, 0)).Content(new Markup(text));
		}).ToArray();
		return new WrapHStack(panels);
	}
}
