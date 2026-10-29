using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Graphs;

namespace Basil.Host.Cli.Tests.Dashboard;

/// <summary>The history buffer, the braille graph and the meter.</summary>
public sealed class GraphTests
{
	[Fact]
	public void A_series_keeps_the_most_recent_samples_oldest_first()
	{
		var series = new Series(3);
		foreach (var value in new double[] { 1, 2, 3, 4, 5 }) series.Add(value);

		Assert.Equal([3, 4, 5], series.Values());
		Assert.Equal(5, series.Latest);
		Assert.Equal(5, series.Max());
		Assert.Equal(3, series.Count);
	}

	[Fact]
	public void An_empty_series_is_zero()
	{
		var series = new Series(3);

		Assert.Empty(series.Values());
		Assert.Equal(0, series.Latest);
		Assert.Equal(0, series.Max());
	}

	[Fact]
	public void The_dashboard_keeps_fifteen_minutes_of_one_second_samples()
	{
		Assert.Equal(900, DashboardModel.HistorySeconds);
		var model = new DashboardModel();
		for (var i = 0; i < 1000; i++) model.Cpu.Add(i);

		Assert.Equal(900, model.Cpu.Count);
		Assert.Equal(100, model.Cpu.Values()[0]);
	}

	[Fact]
	public void A_value_at_the_maximum_fills_every_dot_of_the_column()
	{
		var surface = new GridSurface(2, 1);

		BrailleGraph.Draw(surface, new Area(0, 0, 2, 1), [10, 10, 10, 10], 10);

		Assert.Equal('⣿', surface.CharAt(0, 0));
		Assert.Equal('⣿', surface.CharAt(1, 0));
		Assert.Equal(Tone.Heat, surface.ToneAt(0, 0));
	}

	[Fact]
	public void A_value_of_zero_fills_only_the_bottom_dot_row()
	{
		var surface = new GridSurface(1, 1);

		BrailleGraph.Draw(surface, new Area(0, 0, 1, 1), [0, 0], 10);

		// The bottom dot row of a braille cell is dots 7 and 8: 0x40 | 0x80.
		Assert.Equal('⣀', surface.CharAt(0, 0));
	}

	[Fact]
	public void The_newest_sample_is_at_the_right_edge_and_older_columns_stay_blank()
	{
		var surface = new GridSurface(3, 1);

		BrailleGraph.Draw(surface, new Area(0, 0, 3, 1), [10, 10], 10);

		Assert.Equal(' ', surface.CharAt(0, 0));
		Assert.Equal(' ', surface.CharAt(1, 0));
		Assert.Equal('⣿', surface.CharAt(2, 0));
	}

	[Fact]
	public void A_value_above_the_maximum_is_clamped_to_the_top()
	{
		var surface = new GridSurface(1, 1);

		BrailleGraph.Draw(surface, new Area(0, 0, 1, 1), [1000, 1000], 10);

		Assert.Equal('⣿', surface.CharAt(0, 0));
	}

	[Fact]
	public void A_graph_taller_than_one_row_rises_with_the_value()
	{
		var surface = new GridSurface(1, 2);

		BrailleGraph.Draw(surface, new Area(0, 0, 1, 2), [2, 2], 10);

		Assert.Equal(' ', surface.CharAt(0, 0));
		Assert.NotEqual(' ', surface.CharAt(0, 1));
	}

	[Fact]
	public void The_meter_fills_in_proportion_and_shows_its_caption()
	{
		var surface = new GridSurface(40, 1);

		Meter.Draw(surface, new Area(0, 0, 40, 1), "cpu", 0.5, "50%");

		var row = surface.Row(0);
		Assert.StartsWith("cpu [", row);
		Assert.EndsWith("] 50%", row);
		var bar = row[(row.IndexOf('[') + 1)..row.IndexOf(']')];
		Assert.Equal(bar.Length / 2, bar.Count(c => c == '█'));
	}

	[Fact]
	public void Drawing_in_an_empty_area_does_nothing()
	{
		var surface = new GridSurface(5, 5);

		BrailleGraph.Draw(surface, new Area(0, 0, 0, 3), [1, 2], 10);
		Meter.Draw(surface, new Area(0, 0, 3, 1), "label", 1, "100%");

		Assert.DoesNotContain('⣿', surface.ToString());
	}
}
