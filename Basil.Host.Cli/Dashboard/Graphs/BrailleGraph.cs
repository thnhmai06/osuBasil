namespace Basil.Host.Cli.Dashboard.Graphs;

public static class BrailleGraph
{
	private const int DotsPerColumn = 2;
	private const int DotsPerRow = 4;

	public const int MinimumWidthForGraphs = 100;

	private static readonly int[,] Bits = { { 0x01, 0x02, 0x04, 0x40 }, { 0x08, 0x10, 0x20, 0x80 } };

	public static void Draw(IPaintSurface surface, Area area, IReadOnlyList<double> values, double maximum)
	{
		if (area.Width <= 0 || area.Height <= 0) return;

		var columns = area.Width * DotsPerColumn;
		var rows = area.Height * DotsPerRow;
		var top = Math.Max(maximum, 1e-9);
		var cells = new int[area.Width, area.Height];
		var heat = new double[area.Width];
		var shown = values.Count > columns ? values.Skip(values.Count - columns).ToArray() : [.. values];
		var offset = columns - shown.Length;
		for (var i = 0; i < shown.Length; i++)
		{
			var dotX = offset + i;
			var level = Math.Clamp(shown[i] / top, 0, 1);
			var dotY = rows - 1 - (int)Math.Round(level * (rows - 1));
			// Fill from the value down to the bottom so the graph reads as an area, as btop does.
			for (var y = dotY; y < rows; y++)
				cells[dotX / DotsPerColumn, y / DotsPerRow] |= Bits[dotX % DotsPerColumn, y % DotsPerRow];

			heat[dotX / DotsPerColumn] = Math.Max(heat[dotX / DotsPerColumn], level);
		}

		for (var x = 0; x < area.Width; x++)
		for (var y = 0; y < area.Height; y++)
		{
			var bits = cells[x, y];
			if (bits != 0) surface.Put(area.X + x, area.Y + y, (char)(0x2800 + bits), Tone.Heat, heat[x]);
		}
	}
}

public static class Meter
{
	public static void Draw(IPaintSurface surface, Area area, string label, double fraction, string caption)
	{
		var barWidth = area.Width - label.Length - caption.Length - 4;
		surface.Text(area, 0, 0, label, Tone.Muted);
		if (barWidth < 2)
		{
			surface.Text(area, label.Length + 1, 0, caption);
			return;
		}

		var start = label.Length + 1;
		surface.Put(area.X + start, area.Y, '[', Tone.Muted);
		var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * barWidth);
		for (var i = 0; i < barWidth; i++)
		{
			var heat = (i + 1d) / barWidth;
			if (i < filled) surface.Put(area.X + start + 1 + i, area.Y, '█', Tone.Heat, heat);
			else surface.Put(area.X + start + 1 + i, area.Y, '·', Tone.Muted);
		}

		surface.Put(area.X + start + 1 + barWidth, area.Y, ']', Tone.Muted);
		surface.Text(area, start + barWidth + 3, 0, caption);
	}
}