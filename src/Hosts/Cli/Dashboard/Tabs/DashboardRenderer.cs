using System.Globalization;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Graphs;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>Paints the dashboard on a surface: the tab bar, the tab in view and the output of the command box.</summary>
/// <param name="model">What to show.</param>
/// <param name="formatter">Shows times in the operator's time zone and masks as names.</param>
public sealed class DashboardRenderer(DashboardModel model, ViewFormatter formatter)
{
	private static readonly DashboardTab[] Tabs = Enum.GetValues<DashboardTab>();

	/// <summary>The rows the command output takes at most.</summary>
	public const int OutputRows = 8;

	/// <summary>Paints everything.</summary>
	/// <param name="surface">Where to draw.</param>
	/// <param name="showOutput">Whether the output of the command box has a place at the bottom.</param>
	public void Paint(IPaintSurface surface, bool showOutput)
	{
		var all = new Area(0, 0, surface.Width, surface.Height);
		PaintTabBar(surface, all.Top(1));
		var body = all.Below(1);
		if (showOutput && body.Height > OutputRows + 6)
		{
			PaintOutput(surface, new Area(body.X, body.Bottom - OutputRows, body.Width, OutputRows));
			body = body.Top(body.Height - OutputRows);
		}

		if (model.ShutdownStatus.Length > 0)
		{
			surface.Text(body, 0, 0, model.ShutdownStatus, Tone.Warn);
			body = body.Below(1);
		}
		else if (model.Notice.Length > 0)
		{
			surface.Text(body, 0, 0, model.Notice, Tone.Warn);
			body = body.Below(1);
		}

		switch (model.Tab)
		{
			case DashboardTab.Overview: PaintOverview(surface, body); break;
			case DashboardTab.Users: PaintUsers(surface, body); break;
			case DashboardTab.Rooms: PaintRooms(surface, body); break;
			case DashboardTab.Metrics: PaintMetrics(surface, body); break;
			case DashboardTab.Logs: PaintLogs(surface, body); break;
			case DashboardTab.Chat: PaintChat(surface, body); break;
		}
	}

	private void PaintTabBar(IPaintSurface surface, Area area)
	{
		var column = 0;
		for (var i = 0; i < Tabs.Length; i++)
		{
			var title = $" F{i + 1} {Tabs[i]} ";
			column = surface.Text(area, column, 0, title, Tabs[i] == model.Tab ? Tone.Selected : Tone.Muted) - area.X;
		}

		surface.Text(area, column + 1, 0, $"Basil {model.Version}".Fit(Math.Max(area.Width - column - 1, 0)), Tone.Accent);
	}

	private void PaintOutput(IPaintSurface surface, Area area)
	{
		surface.Rule(area, 0, '─');
		var lines = model.Output.TakeLast(area.Height - 1).ToList();
		for (var i = 0; i < lines.Count; i++)
			surface.Text(area, 0, 1 + i, lines[i].Text.Fit(area.Width), lines[i].Kind switch
			{
				OutputKind.Error => Tone.Bad,
				OutputKind.Ok => Tone.Good,
				OutputKind.Header => Tone.Accent,
				OutputKind.Field => Tone.Text,
				_ => Tone.Text
			});
	}

	private void PaintOverview(IPaintSurface surface, Area area)
	{
		var wide = BrailleGraph.FitsGraphs(area.Width);
		var summary = area.Top(7);
		surface.Title(summary, "Overview");
		surface.Text(summary, 0, 1, $"users online : {model.UsersOnline.Latest:0}    rooms : {model.Rooms.Latest:0}    requests/s : {model.Requests.Latest:0.#}");
		surface.Text(summary, 0, 2, "connections  : " + (model.Sessions.Count == 0
			? "-"
			: string.Join("  ", model.Sessions.Where(pair => pair.Key != "bot").Select(pair => $"{pair.Key} {pair.Value}"))));
		Meter.Draw(surface, new Area(summary.X, summary.Y + 3, Math.Min(summary.Width, 60), 1), "cpu   ", model.Cpu.Latest / 100, $"{model.Cpu.Latest:0.#}%");
		Meter.Draw(surface, new Area(summary.X, summary.Y + 4, Math.Min(summary.Width, 60), 1), "memory",
			model.Memory.Max() <= 0 ? 0 : model.Memory.Latest / model.Memory.Max(), $"{model.Memory.Latest:0} MB");
		var rest = area.Below(7);
		if (!wide)
		{
			PaintCores(surface, rest.Top(Math.Min(model.Cores.Length + 1, Math.Max(rest.Height / 2, 1))));
			PaintRecentLog(surface, rest.Below(Math.Min(model.Cores.Length + 1, Math.Max(rest.Height / 2, 1))));
			return;
		}

		var graphs = rest.Left((rest.Width * 2 / 3));
		var side = rest.After((rest.Width * 2 / 3) + 1);
		var each = Math.Max(graphs.Height / 4, 3);
		Graph(surface, new Area(graphs.X, graphs.Y, graphs.Width, each), "cpu %", model.Cpu, 100);
		Graph(surface, new Area(graphs.X, graphs.Y + each, graphs.Width, each), "memory MB", model.Memory, Math.Max(model.Memory.Max(), 1));
		Graph(surface, new Area(graphs.X, graphs.Y + (2 * each), graphs.Width, each), "users online", model.UsersOnline, Math.Max(model.UsersOnline.Max(), 10));
		Graph(surface, new Area(graphs.X, graphs.Y + (3 * each), graphs.Width, graphs.Height - (3 * each)), "rooms", model.Rooms, Math.Max(model.Rooms.Max(), 5));
		PaintCores(surface, side.Top(Math.Min(model.Cores.Length + 1, side.Height / 2)));
		PaintRecentLog(surface, side.Below(Math.Min(model.Cores.Length + 1, side.Height / 2)));
	}

	private static void Graph(IPaintSurface surface, Area area, string title, Series series, double maximum)
	{
		if (area.Height < 2) return;

		surface.Text(area, 0, 0, $"{title}  {series.Latest:0.#}", Tone.Accent);
		BrailleGraph.Draw(surface, area.Below(1), series.Values(), maximum);
	}

	private void PaintCores(IPaintSurface surface, Area area)
	{
		if (area.Height < 1) return;

		surface.Title(area, "cores");
		for (var i = 0; i < model.Cores.Length && i + 1 < area.Height; i++)
			Meter.Draw(surface, new Area(area.X, area.Y + 1 + i, Math.Min(area.Width, 40), 1), $"cpu{i,-2}", model.Cores[i] / 100, $"{model.Cores[i]:0}%");
	}

	private void PaintRecentLog(IPaintSurface surface, Area area)
	{
		if (area.Height < 2) return;

		surface.Title(area, "recent log");
		var lines = model.Log.Lines().TakeLast(area.Height - 1).ToList();
		for (var i = 0; i < lines.Count; i++) PaintLogLine(surface, area, 1 + i, lines[i]);
	}

	private void PaintLogLine(IPaintSurface surface, Area area, int row, LogLine line)
	{
		var tone = line.Level switch
		{
			LogLevel.Critical or LogLevel.Error => Tone.Bad,
			LogLevel.Warning => Tone.Warn,
			LogLevel.Debug or LogLevel.Trace => Tone.Muted,
			_ => Tone.Text
		};
		var text = $"{formatter.Time(line.Time)[11..]} {LevelCode(line.Level)} {line.Message}";
		surface.Text(area, 0, row, text.Fit(area.Width), tone);
	}

	private static string LevelCode(LogLevel level)
	{
		return level switch
		{
			LogLevel.Critical => "CRT", LogLevel.Error => "ERR", LogLevel.Warning => "WRN", LogLevel.Information => "INF",
			LogLevel.Debug => "DBG", _ => "TRC"
		};
	}

	private void PaintUsers(IPaintSurface surface, Area area)
	{
		var users = model.Users.Where(user => Matches(model.Filter, user.Name, user.Id.ToString(), user.Connections)).ToList();
		surface.Title(area, $"Users online: {users.Count}" + (model.Filter.Length > 0 ? $"   filter: {model.Filter}" : "") + "   (Alt+Up/Down select, Alt+Enter copies the id)");
		PaintTable(surface, area.Below(1), ["id", "name", "connections", "status", "room", "idle", "permissions"],
			[.. users.Select(user => (IReadOnlyList<string>)[user.Id.ToString(), user.Name, user.Connections, user.Status, user.Room,
				user.Idle.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture), user.Permissions.ToString(CultureInfo.InvariantCulture)])]);
	}

	private void PaintRooms(IPaintSurface surface, Area area)
	{
		var rooms = model.RoomList.Where(room => Matches(model.Filter, room.Name, room.Id.ToString(), room.Host)).ToList();
		surface.Title(area, $"Rooms: {rooms.Count}" + (model.Filter.Length > 0 ? $"   filter: {model.Filter}" : ""));
		var list = area.Below(1).Left(Math.Max(area.Width / 2, 20));
		PaintTable(surface, list, ["id", "name", "players", "state", "host"],
			[.. rooms.Select(room => (IReadOnlyList<string>)[room.Id.ToString(), room.Name, $"{room.Players}/{room.Size}", room.State, room.Host])]);
		var detail = area.Below(1).After(list.Width + 2);
		if (rooms.Count == 0 || detail.Width < 10) return;

		var selected = rooms[Math.Clamp(model.Selected, 0, rooms.Count - 1)];
		surface.Text(detail, 0, 0, $"slots of #{selected.Id} {selected.Name}", Tone.Accent);
		for (var i = 0; i < selected.Slots.Count && i + 1 < detail.Height; i++)
			surface.Text(detail, 0, 1 + i, $"{selected.Slots[i].Index,2}  {selected.Slots[i].Player,-16} {selected.Slots[i].State}".Fit(detail.Width));
	}

	private void PaintMetrics(IPaintSurface surface, Area area)
	{
		var rows = area;
		if (BrailleGraph.FitsGraphs(area.Width) && area.Height >= 16)
		{
			var half = (area.Width / 2) - 1;
			Graph(surface, new Area(area.X, area.Y, half, 5), "cpu %", model.Cpu, 100);
			Graph(surface, new Area(area.X + half + 2, area.Y, half, 5), "requests/s", model.Requests, Math.Max(model.Requests.Max(), 10));
			Graph(surface, new Area(area.X, area.Y + 5, half, 5), "memory MB", model.Memory, Math.Max(model.Memory.Max(), 1));
			Graph(surface, new Area(area.X + half + 2, area.Y + 5, half, 5), "heap MB", model.Heap, Math.Max(model.Heap.Max(), 1));
			rows = area.Below(10);
		}

		surface.Title(rows, "Diagnostics (the categories of /v1/diagnostics)");
		var body = rows.Below(1);
		var columns = Math.Max(body.Width / 46, 1);
		var perColumn = Math.Max(body.Height, 1);
		for (var i = 0; i < model.Diagnostics.Count; i++)
		{
			var column = i / perColumn;
			if (column >= columns) break;

			var (name, value) = model.Diagnostics[i];
			surface.Text(body, column * 46, i % perColumn, $"{name,-30} {value}".Fit(45));
		}
	}

	private void PaintLogs(IPaintSurface surface, Area area)
	{
		var lines = (model.LogPaused && model.LogFrozen is not null ? model.LogFrozen : model.Log.Lines())
			.Where(line => line.Level >= model.MinimumLevel && Matches(model.Filter, line.Message, line.Category)).ToList();
		surface.Title(area, $"Log  level >= {model.MinimumLevel}" + (model.Filter.Length > 0 ? $"   filter: {model.Filter}" : "") + (model.LogPaused ? "   [paused]" : ""));
		var shown = lines.TakeLast(area.Height - 1).ToList();
		for (var i = 0; i < shown.Count; i++) PaintLogLine(surface, area, 1 + i, shown[i]);
	}

	private void PaintChat(IPaintSurface surface, Area area)
	{
		var lines = model.Chat.Lines().Where(line => Matches(model.Filter, line.Channel, line.Author, line.Text)).ToList();
		surface.Title(area, "Chat (read only)" + (model.Filter.Length > 0 ? $"   filter: {model.Filter}" : ""));
		var shown = lines.TakeLast(area.Height - 1).ToList();
		for (var i = 0; i < shown.Count; i++)
			surface.Text(area, 0, 1 + i, $"{formatter.Time(shown[i].Time)[11..]} {shown[i].Channel} <{shown[i].Author}> {shown[i].Text}".Fit(area.Width));
	}

	private void PaintTable(IPaintSurface surface, Area area, string[] header, IReadOnlyList<IReadOnlyList<string>> rows)
	{
		if (area.Height < 1) return;

		var widths = header.Select((title, index) => Math.Min(Math.Max(title.Length, rows.Count == 0 ? 0 : rows.Max(row => row[index].Length)), 24)).ToArray();
		string Line(IReadOnlyList<string> cells)
		{
			return string.Join("  ", cells.Select((cell, index) => cell.Fit(widths[index]).PadRight(widths[index])));
		}

		surface.Text(area, 0, 0, Line(header).Fit(area.Width), Tone.Accent);
		var visible = area.Height - 1;
		var selected = Math.Clamp(model.Selected, 0, Math.Max(rows.Count - 1, 0));
		var first = Math.Clamp(selected - visible + 1, 0, Math.Max(rows.Count - visible, 0));
		for (var i = 0; i < visible && first + i < rows.Count; i++)
			surface.Text(area, 0, 1 + i, Line(rows[first + i]).Fit(area.Width), first + i == selected ? Tone.Selected : Tone.Text);
	}

	private static bool Matches(string filter, params string[] fields)
	{
		return filter.Length == 0 || fields.Any(field => field.Contains(filter, StringComparison.OrdinalIgnoreCase));
	}
}
