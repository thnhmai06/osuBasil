using System.Globalization;
using Basil.Application.Diagnostics.Services;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     The machine and the server: eight graphs over the last fifteen minutes, a box of measurements for each category
///     under them, and at the end of the page the explanation of the measurement that is selected. The whole page
///     scrolls; the graphs and the boxes stand in the same columns.
/// </summary>
internal sealed class MonitorTab : ITabView
{
	private const int WarningPercent = 70;
	private const int ErrorPercent = 90;
	private const int BoxHeight = 8;
	private const int MinimumGraphHeight = 6;
	private readonly WheelAcceleration _wheel;
	private readonly Glyphs _glyphs;
	private readonly DashboardModel _model;
	private readonly bool _noColor;
	private readonly Palette _palette;
	private readonly List<GraphPanel> _panels = [];
	private readonly Func<(int Columns, int Rows)> _size;
	private readonly Func<Theme> _theme;
	private readonly Grid _page = new Grid().Stretch();
	private readonly List<GridCell> _graphCells = [];
	private readonly List<Box> _boxes = [];
	private readonly Dictionary<string, (string Category, MetricLine Line)> _known = [];
	private readonly State<bool> _detailOpen = new(false);
	private readonly State<int> _detailHeight = new(4);
	private readonly State<int> _graphHeight = new(MinimumGraphHeight);
	private readonly State<string> _detailTitle = new(string.Empty);
	private readonly State<string> _detailText = new(string.Empty);
	private readonly Group _detail;
	private readonly ScrollViewer _scroll;
	private (int Width, int Across, int GraphHeight, int Boxes)? _arranged;
	private string? _selected;
	private object? _seen;

	/// <summary>Creates the tab.</summary>
	/// <param name="model">What the dashboard sampled.</param>
	/// <param name="themes">The theme that colours the graphs.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="options">Tells whether the dashboard is monochrome.</param>
	/// <param name="size">The size of the terminal in columns and rows; it must read the state that changes on resize.</param>
	/// <param name="clock">Measures the time between the notches of the mouse wheel.</param>
	public MonitorTab(DashboardModel model, ThemeStore themes, Glyphs glyphs, CliOptions options,
		Func<(int Columns, int Rows)> size, TimeProvider clock)
	{
		_wheel = new WheelAcceleration(clock);
		_model = model;
		_glyphs = glyphs;
		_size = size;
		_noColor = options.NoColor;
		_palette = new Palette(options.NoColor);
		_theme = () => themes.Theme;
		var cpu = Panel("CPU", model.MachineCpu, model.Cpu, () => 100,
			() => $"{Percent(model.MachineCpu.Latest)} / {Percent(model.Cpu.Latest)}", Legend("Machine", "Process"));
		var ram = Panel("RAM", model.MachineMemory, model.ProcessMemory, Total, RamHeadline, Legend("Used", "Process"));
		var network = Panel("Network", model.NetworkReceived, model.NetworkSent,
			() => Math.Max(Math.Max(model.NetworkReceived.Max(), model.NetworkSent.Max()) * 1.1, 0.1), NetworkHeadline,
			Legend("Receive", "Send"));
		var open = Open("Connections", model.ConnectionsOpen);
		var graphs = new List<GraphPanel>
		{
			cpu, ram, network, open, Open("Stable", model.OpenByKind["stable"]), Open("Tourney", model.OpenByKind["stabletourney"]),
			Open("IRC", model.OpenByKind["irc"]), Open("API", model.OpenByKind["api"])
		};
		foreach (var graph in graphs)
		{
			var cell = new GridCell(graph.View.MinHeight(() => _graphHeight.Value));
			_graphCells.Add(cell);
			_page.Cells.Add(cell);
		}

		foreach (var category in DiagnosticsSample.Categories) AddBox(category);

		var style = glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Single;
		_detail = new Group(new Markup(() => _detailTitle.Value)).Style(style).Padding(new Thickness(1, 0, 1, 0))
			.Content(new ScrollViewer(new Markup(() => _detailText.Value).Wrap(true)).Stretch()).IsVisible(_detailOpen)
			.MinHeight(() => _detailHeight.Value).MaxHeight(() => _detailHeight.Value).HorizontalAlignment(Align.Stretch);
		_scroll = new ScrollViewer(_page).Stretch();
		_scroll.HorizontalScrollEnabled(false);
		Arrange();
		// The explanation is docked under the page, outside what scrolls, like the frame of the list tabs.
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(_scroll, 0, 0)
			.Cell(_detail, 1, 0)
			.Stretch();
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
		Arrange();
		if (!ReferenceEquals(_seen, _model.Diagnostics)) Update();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		Update();
	}

	// Every graph and every box has the same size, and they share the columns: four across when there is room, else two.
	private void Arrange()
	{
		var (columns, rows) = _size();
		var across = columns >= Layout.WideSize.Width ? 4 : 2;
		var graphHeight = Math.Max(MinimumGraphHeight, rows / 3);
		_detailHeight.Value = Math.Max(rows / 3, 4);
		_graphHeight.Value = graphHeight;
		// The page and the scroll bar take three columns; the rest is shared exactly, the first columns taking the remainder.
		var width = Math.Max(columns - 3, across);
		if (_arranged == (width, across, graphHeight, _boxes.Count)) return;

		_arranged = (width, across, graphHeight, _boxes.Count);
		_page.Columns([
			.. Enumerable.Range(0, across).Select(i => new ColumnDefinition
				{ Width = GridLength.Fixed(width / across + (i < width % across ? 1 : 0)) })
		]);
		var graphRows = _graphCells.Count / across;
		var boxRows = (_boxes.Count + across - 1) / across;
		_page.Rows([
			.. Enumerable.Range(0, graphRows).Select(_ => new RowDefinition { Height = GridLength.Fixed(graphHeight) }),
			.. Enumerable.Range(0, boxRows).Select(_ => new RowDefinition { Height = GridLength.Fixed(BoxHeight) })
		]);
		for (var i = 0; i < _graphCells.Count; i++)
		{
			_graphCells[i].Row = i / across;
			_graphCells[i].Column = i % across;
		}

		for (var i = 0; i < _boxes.Count; i++)
		{
			_boxes[i].Cell.Row = graphRows + i / across;
			_boxes[i].Cell.Column = i % across;
		}
	}

	private void Update()
	{
		_seen = _model.Diagnostics;
		foreach (var panel in _panels) panel.Refresh();
		var items = _model.Diagnostics.ToLookup(line => line.Name.Split('.')[0]);
		foreach (var category in items.Select(group => group.Key).Where(key => _boxes.All(box => box.Category != key)))
			AddBox(category);

		_known.Clear();
		foreach (var box in _boxes)
		{
			var lines = Lines(box.Category, items[box.Category].ToList());
			foreach (var line in lines) _known[line.Key] = (box.Category, line);

			box.List.SetLines(lines);
			box.List.Select(_selected is not null && _known.ContainsKey(_selected) ? _selected : null);
		}

		Arrange();
		if (_detailOpen.Value && _selected is not null) FillDetail(_selected);
	}

	private void AddBox(string category)
	{
		var list = new MetricList(_glyphs, _noColor);
		list.Picked += Pick;
		list.AddCommand(new Command
		{
			Id = "monitor.close", LabelMarkup = "Close", Gesture = new KeyGesture(TerminalKey.Escape),
			CanExecute = _ => _detailOpen.Value, Execute = _ => _detailOpen.Value = false
		});
		var inner = new ScrollViewer(list).Stretch();
		// A box that cannot scroll further in the direction of the wheel lets the page take the notch.
		inner.PointerWheelRouted += (_, e) =>
		{
			if (e.RoutingPhase != RoutingPhase.Preview || e.WheelDelta == 0 || e.Handled) return;

			var up = e.WheelDelta > 0;
			if (up ? list.Scroll.OffsetY > 0 : list.Scroll.OffsetY < list.Scroll.ExtentHeight - list.Scroll.ViewportHeight) return;

			e.Handled = true;
			var step = _wheel.Step();
			_scroll.VerticalOffset = Math.Max(_scroll.VerticalOffset + (up ? -step : step), 0);
		};
		var group = new Group(MonitorMetrics.Title(category)).Style(_glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded)
			.Content(inner).Stretch();
		var cell = new GridCell(group);
		_page.Cells.Add(cell);
		_boxes.Add(new Box(category, list, cell));
	}

	private void Pick(string key)
	{
		if (key.Length == 0 || !_known.ContainsKey(key)) return;

		_selected = key;
		foreach (var box in _boxes) box.List.Select(key);

		_detailOpen.Value = true;
		FillDetail(key);
	}

	private List<MetricLine> Lines(string category, List<(string Name, string Value)> items)
	{
		var lines = new List<MetricLine>();
		foreach (var (name, value) in items)
		{
			if (name == "database.connectionsMeasured") continue;

			var info = MonitorMetrics.Of(name);
			if (info.Parts is not null)
			{
				var numbers = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
				for (var i = 0; i < numbers.Length; i++)
				{
					var key = $"{name}#{i}";
					var part = MonitorMetrics.Of(key);
					lines.Add(new MetricLine(key, string.Empty, part.Label, MonitorMetrics.Show(part, numbers[i], _glyphs.Ascii), Ink.Text));
				}

				continue;
			}

			var parts = name.Split('.');
			var group = parts.Length >= 3 ? MonitorMetrics.GroupTitle(parts[1]) : string.Empty;
			var alarm = info.Alarm && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0;
			lines.Add(new MetricLine(name, group, info.Label, MonitorMetrics.Show(info, value, _glyphs.Ascii),
				alarm ? Ink.Warning : Ink.Text));
		}

		if (category == "exceptions")
		{
			var total = _model.ExceptionsLastMinute.Sum(count => count.Count);
			lines.Add(new MetricLine(MonitorMetrics.LastExceptions, string.Empty,
				MonitorMetrics.Of(MonitorMetrics.LastExceptions).Label, total == 0 ? "-" : $"×{total}", Ink.Text));
		}

		if (category == "database" && !items.Any(item => item is { Name: "database.connectionsMeasured", Value: "1" }))
		{
			lines.RemoveAll(line => line.Key is "database.connectionsInUse" or "database.connectionsIdle");
			foreach (var key in new[] { "database.connectionsInUse", "database.connectionsIdle" })
				lines.Add(new MetricLine(key, string.Empty, MonitorMetrics.Of(key).Label, "not measured", Ink.Muted));
		}

		if (lines.Count == 0) lines.Add(new MetricLine(string.Empty, string.Empty, "No data yet", string.Empty, Ink.Muted));

		return lines;
	}

	private void FillDetail(string key)
	{
		if (!_known.TryGetValue(key, out var found)) return;

		var (category, line) = found;
		var info = MonitorMetrics.Of(key);
		var path = new List<string> { MonitorMetrics.Title(category) };
		if (line.Group.Length > 0) path.Add(line.Group);

		path.Add(line.Label);
		var crumb = $" {_palette.Wrap(Ink.Muted, _glyphs.Crumb)} ";
		_detailTitle.Value = string.Join(crumb, path.Select(Palette.Escape));
		var lines = new List<string>();
		if (key == MonitorMetrics.LastExceptions)
		{
			lines.Add(_palette.Wrap(Ink.Muted, "Value"));
			lines.AddRange(_model.ExceptionsLastMinute.Count == 0
				? [_palette.Wrap(Ink.Muted, "none in the last minute")]
				: _model.ExceptionsLastMinute.Select(count => _palette.Wrap(Ink.Text, $"{count.Type} ×{count.Count}")));
		}
		else
		{
			lines.Add(_palette.Wrap(Ink.Muted, "Value ") + _palette.Wrap(line.Ink, line.Value));
		}

		lines.Add(_palette.Wrap(Ink.Muted, "Key   ") + _palette.Wrap(Ink.Muted, key));
		lines.Add(string.Empty);
		lines.Add(_palette.Wrap(Ink.Text, info.Description));
		_detailText.Value = string.Join('\n', lines);
	}

	private GraphPanel Panel(string title, Series primary, Series? secondary, Func<double> ceiling, Func<string> headline,
		string legend)
	{
		var panel = new GraphPanel(title, primary, secondary, ceiling, headline, legend, _theme, _glyphs, _noColor);
		_panels.Add(panel);
		return panel;
	}

	private GraphPanel Open(string title, Series series)
	{
		return Panel(title, series, null, () => Math.Max(series.Max() * 1.1, 1),
			() => _palette.Wrap(Ink.Accent, series.Latest.ToString("0", CultureInfo.InvariantCulture)), Legend("Open", null));
	}

	private string Legend(string primary, string? secondary)
	{
		var first = $"{_palette.Wrap(Ink.Accent, GraphPanel.PrimaryMark(_glyphs))} {Palette.Escape(primary)}";
		return secondary is null
			? first
			: $"{first}   {_palette.Wrap(Ink.Accent, GraphPanel.SecondaryMark(_glyphs))} {Palette.Escape(secondary)}";
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
		return $"{down} {_model.NetworkReceived.Latest:0.0} {up} {_model.NetworkSent.Latest:0.0} MB/s";
	}

	private sealed record Box(string Category, MetricList List, GridCell Cell);
}
