using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Graphs;
using Basil.Host.Cli.Dashboard.Tabs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>What the dashboard paints for the data it is given, checked on an in-memory surface.</summary>
public sealed class DashboardRenderingTests
{
	private static readonly DateTimeOffset Moment = new(2026, 1, 1, 12, 30, 15, TimeSpan.Zero);

	private static (DashboardModel Model, DashboardRenderer Renderer) Create()
	{
		var model = new DashboardModel { Log = new LogBuffer(new FakeTimeProvider(Moment)) };
		return (model, new DashboardRenderer(model, new ViewFormatter(TimeZoneInfo.Utc)));
	}

	private static GridSurface Paint(DashboardModel model, DashboardRenderer renderer, int width = 120, int height = 40,
		bool output = false)
	{
		var surface = new GridSurface(width, height);
		renderer.Paint(surface, output);
		return surface;
	}

	[Fact]
	public void The_tab_bar_names_the_six_tabs_and_marks_the_one_in_view()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Logs;

		var surface = Paint(model, renderer);

		foreach (var name in new[] { "Overview", "Users", "Rooms", "Metrics", "Logs", "Chat" })
			Assert.Contains(name, surface.Row(0));
		var at = surface.Row(0).IndexOf(" F5 Logs ", StringComparison.Ordinal);
		Assert.Equal(Tone.Selected, surface.ToneAt(at + 1, 0));
	}

	[Fact]
	public void The_users_tab_lists_the_accounts_online_and_marks_the_selected_row()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Users;
		model.Users =
		[
			new UserRow(5, "alice", "osu irc", "Idle", "#3", TimeSpan.FromSeconds(75), 12),
			new UserRow(6, "bob", "api", "-", "-", TimeSpan.Zero, 0)
		];
		model.Selected = 1;

		var surface = Paint(model, renderer);

		Assert.Contains("Users online: 2", surface.Row(1));
		Assert.Contains("alice", surface.Row(3));
		Assert.Contains("osu irc", surface.Row(3));
		Assert.Contains("00:01:15", surface.Row(3));
		Assert.Contains("bob", surface.Row(4));
		Assert.Equal(Tone.Selected, surface.ToneAt(0, 4));
		Assert.NotEqual(Tone.Selected, surface.ToneAt(0, 3));
	}

	[Fact]
	public void The_users_filter_keeps_the_matching_rows_only()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Users;
		model.Users =
		[
			new UserRow(5, "alice", "osu", "-", "-", TimeSpan.Zero, 0),
			new UserRow(6, "bob", "osu", "-", "-", TimeSpan.Zero, 0)
		];
		model.Filter = "bo";

		var text = Paint(model, renderer).ToString();

		Assert.Contains("bob", text);
		Assert.DoesNotContain("alice", text);
	}

	[Fact]
	public void The_rooms_tab_lists_rooms_and_the_slots_of_the_selected_one()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Rooms;
		model.RoomList =
		[
			new RoomRow(1, "first", 1, 8, "waiting", "alice", [new SlotRow(1, "alice", "notReady")]),
			new RoomRow(2, "second", 0, 8, "locked", "-", [new SlotRow(1, "-", "open")])
		];
		model.Selected = 0;

		var text = Paint(model, renderer).ToString();

		Assert.Contains("first", text);
		Assert.Contains("second", text);
		Assert.Contains("slots of #1 first", text);
		Assert.Contains("notReady", text);
	}

	[Fact]
	public void The_log_tab_colours_lines_by_level_filters_by_level_and_text_and_can_stand_still()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Logs;
		model.Log.Add(new LogLine(Moment, LogLevel.Debug, "c", "debug hidden"));
		model.Log.Add(new LogLine(Moment, LogLevel.Information, "c", "started up"));
		model.Log.Add(new LogLine(Moment, LogLevel.Warning, "c", "careful now"));
		model.Log.Add(new LogLine(Moment, LogLevel.Error, "c", "broken thing"));

		var surface = Paint(model, renderer);
		var rows = Enumerable.Range(0, 10).Select(surface.Row).ToList();

		Assert.DoesNotContain(rows, row => row.Contains("debug hidden"));
		Assert.Equal(Tone.Warn, surface.ToneAt(0, rows.FindIndex(row => row.Contains("careful now"))));
		Assert.Equal(Tone.Bad, surface.ToneAt(0, rows.FindIndex(row => row.Contains("broken thing"))));
		Assert.Contains("12:30:15 INF started up", string.Join('\n', rows));

		model.Filter = "broken";
		Assert.DoesNotContain("started up", Paint(model, renderer).ToString());

		model.Filter = string.Empty;
		model.LogPaused = true;
		model.LogFrozen = model.Log.Lines();
		model.Log.Add(new LogLine(Moment, LogLevel.Error, "c", "arrived while paused"));
		var paused = Paint(model, renderer).ToString();
		Assert.DoesNotContain("arrived while paused", paused);
		Assert.Contains("[paused]", paused);
	}

	[Fact]
	public void The_chat_tab_shows_channel_author_and_text_and_filters_by_channel()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Chat;
		model.Chat.Add(new ChatLine(Moment, "#osu", "alice", "hello there"));
		model.Chat.Add(new ChatLine(Moment, "#lobby", "bob", "other room"));

		Assert.Contains("12:30:15 #osu <alice> hello there", Paint(model, renderer).ToString());

		model.Filter = "#lobby";
		var text = Paint(model, renderer).ToString();
		Assert.DoesNotContain("hello there", text);
		Assert.Contains("other room", text);
	}

	[Fact]
	public void The_command_output_has_a_place_at_the_bottom_in_the_full_mode_only()
	{
		var (model, renderer) = Create();
		model.AddOutput(new OutputLine(OutputKind.Error, "error: notFound: No such user."));

		Assert.Contains("error: notFound: No such user.", Paint(model, renderer, output: true).ToString());
		Assert.DoesNotContain("error: notFound", Paint(model, renderer, output: false).ToString());
	}

	[Fact]
	public void The_warning_of_the_ctrl_c_rules_is_shown_above_the_tab()
	{
		var (model, renderer) = Create();
		model.Notice = "Press Ctrl+C again within 3 seconds to shut down the server.";

		Assert.Contains(model.Notice, Paint(model, renderer).Row(1));
	}

	[Fact]
	public void A_wide_terminal_draws_the_big_graphs_and_a_narrow_one_hides_them()
	{
		var (model, renderer) = Create();
		for (var i = 0; i < 200; i++) model.Cpu.Add(i % 100);

		var wide = Paint(model, renderer, 140).ToString();
		var narrow = Paint(model, renderer, 80).ToString();

		Assert.Contains(wide, character => character is >= '⠁' and <= '⣿');
		Assert.DoesNotContain(narrow, character => character is >= '⠁' and <= '⣿');
		Assert.True(BrailleGraph.FitsGraphs(100));
		Assert.False(BrailleGraph.FitsGraphs(99));
	}

	[Fact]
	public void The_overview_shows_the_current_numbers_and_one_meter_per_core()
	{
		var (model, renderer) = Create();
		model.UsersOnline.Add(7);
		model.Rooms.Add(3);
		model.Cores = [10, 90];

		var text = Paint(model, renderer, 140).ToString();

		Assert.Contains("users online : 7", text);
		Assert.Contains("rooms : 3", text);
		Assert.Contains("cpu0 ", text);
		Assert.Contains("cpu1 ", text);
		Assert.Contains("90%", text);
	}

	[Fact]
	public void The_metrics_tab_lists_the_diagnostics_by_dotted_name()
	{
		var (model, renderer) = Create();
		model.Tab = DashboardTab.Metrics;
		model.Diagnostics = [("process.cpuPercent", "12.5"), ("application.rooms", "3")];

		var text = Paint(model, renderer, 140).ToString();

		Assert.Contains("process.cpuPercent", text);
		Assert.Contains("12.5", text);
		Assert.Contains("application.rooms", text);
	}

	[Fact]
	public void Painting_on_a_tiny_surface_does_not_throw()
	{
		var (model, renderer) = Create();
		foreach (var tab in Enum.GetValues<DashboardTab>())
		{
			model.Tab = tab;
			Paint(model, renderer, 3, 2);
			Paint(model, renderer, 1, 1);
		}
	}
}