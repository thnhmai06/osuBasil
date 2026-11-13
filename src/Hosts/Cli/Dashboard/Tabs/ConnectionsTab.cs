using System.Globalization;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The connections of each kind: how many are open, what happened to them since the server started, and how fast they are served.</summary>
internal sealed class ConnectionsTab : ITabView
{
	private static readonly string[] Events = ["opened", "closed", "rejected", "timeout", "errors"];
	private readonly DashboardModel _model;
	private readonly Palette _palette;
	private readonly List<GraphPanel> _panels = [];
	private readonly State<int> _stamp = new(0);
	private object? _seen;

	/// <summary>Creates the tab.</summary>
	/// <param name="model">What the dashboard sampled.</param>
	/// <param name="themes">The theme that colours the graphs.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="options">Tells whether the dashboard is monochrome.</param>
	public ConnectionsTab(DashboardModel model, ThemeStore themes, Glyphs glyphs, CliOptions options)
	{
		_model = model;
		_palette = new Palette(options.NoColor);
		var style = glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded;
		var panels = new VStack().Spacing(0);
		foreach (var (title, kind, latency) in new[]
		         {
			         ("osu! stable", "stable", "stable"), ("Tourney", "stabletourney", ""), ("IRC", "irc", "irc"),
			         ("HTTP API", "api", "api")
		         })
		{
			var series = model.OpenByKind[kind];
			var graph = new GraphPanel(title, series, null, () => Math.Max(series.Max() * 1.1, 1),
				() => _palette.Wrap(Ink.Accent, series.Latest.ToString("0", CultureInfo.InvariantCulture)), string.Empty,
				() => themes.Theme, glyphs, options.NoColor, false);
			_panels.Add(graph);
			var info = new Markup(() => Info(kind, latency));
			var body = new Grid()
				.Columns(new ColumnDefinition { Width = GridLength.Star() }, new ColumnDefinition { Width = GridLength.Star() })
				.Rows(new RowDefinition { Height = GridLength.Star() })
				.Cell(graph.View, 0, 0)
				.Cell(new Padder(info).Padding(new Thickness(2, 0, 0, 0)), 0, 1);
			panels.Add(new Group(title).Style(style).Padding(new Thickness(1, 0, 1, 0)).Content(body).MinHeight(9));
		}

		View = new ScrollViewer(panels);
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
		_stamp.Value++;
	}

	private string Info(string kind, string latency)
	{
		string Count(string name)
		{
			return _model.ConnectionEvents.GetValueOrDefault($"{kind}.{name}").ToString("N0", CultureInfo.InvariantCulture);
		}

		_ = _stamp.Value;
		var lines = new List<string> { _palette.Wrap(Ink.Accent, "Events") };
		lines.AddRange(Events.Select(name =>
			$"{_palette.Wrap(Ink.Muted, (char.ToUpperInvariant(name[0]) + name[1..]).PadRight(9))}" +
			_palette.Wrap(name is "errors" or "rejected" or "timeout" && Count(name) != "0" ? Ink.Warning : Ink.Text, Count(name))));
		var duration = latency.Length > 0 && _model.Latencies.TryGetValue(latency, out var value)
			? $"p50 {Ms(value.P50)}  p95 {Ms(value.P95)}  p99 {Ms(value.P99)}"
			: "-";
		lines.Add($"{_palette.Wrap(Ink.Accent, "Duration")} {_palette.Wrap(Ink.Text, duration)}");
		return string.Join('\n', lines);
	}

	private static string Ms(double milliseconds)
	{
		return milliseconds < 10
			? $"{milliseconds.ToString("0.00", CultureInfo.InvariantCulture)} ms"
			: $"{milliseconds.ToString("0", CultureInfo.InvariantCulture)} ms";
	}
}
