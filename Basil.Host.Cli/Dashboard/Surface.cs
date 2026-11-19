using System.Text;

namespace Basil.Host.Cli.Dashboard;

public enum Tone : byte
{
	Text,

	Muted,

	Accent,

	Good,

	Warn,

	Bad,

	Selected,

	Heat
}

public readonly record struct Area(int X, int Y, int Width, int Height)
{
	public int Right => X + Width;

	public int Bottom => Y + Height;

	public Area Top(int rows)
	{
		return this with { Height = Math.Min(rows, Height) };
	}

	public Area Below(int rows)
	{
		return new Area(X, Y + Math.Min(rows, Height), Width, Math.Max(Height - rows, 0));
	}

	public Area Left(int columns)
	{
		return this with { Width = Math.Min(columns, Width) };
	}

	public Area After(int columns)
	{
		return new Area(X + Math.Min(columns, Width), Y, Math.Max(Width - columns, 0), Height);
	}
}

public interface IPaintSurface
{
	int Width { get; }

	int Height { get; }

	void Put(int x, int y, char character, Tone tone, double heat = 0);
}

public static class SurfaceExtensions
{
	extension(IPaintSurface surface)
	{
		public int Text(Area area, int column, int row, string text,
			Tone tone = Tone.Text)
		{
			if (row < 0 || row >= area.Height) return area.X + column;

			var x = area.X + column;
			foreach (var character in text)
			{
				if (x >= area.Right) break;

				if (x >= area.X) surface.Put(x, area.Y + row, character, tone);
				x++;
			}

			return x;
		}

		public void Rule(Area area, int row, char character, Tone tone = Tone.Muted)
		{
			for (var x = 0; x < area.Width; x++) surface.Put(area.X + x, area.Y + row, character, tone);
		}

		public void Title(Area area, string title)
		{
			surface.Text(area, 0, 0, title, Tone.Accent);
		}
	}

	public static string Fit(this string text, int width)
	{
		return width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";
	}
}

public sealed class GridSurface : IPaintSurface
{
	private readonly char[,] _cells;
	private readonly Tone[,] _tones;

	public GridSurface(int width, int height)
	{
		Width = width;
		Height = height;
		_cells = new char[width, height];
		_tones = new Tone[width, height];
		for (var y = 0; y < height; y++)
		for (var x = 0; x < width; x++)
			_cells[x, y] = ' ';
	}

	public int Width { get; }

	public int Height { get; }

	public void Put(int x, int y, char character, Tone tone, double heat = 0)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height) return;

		_cells[x, y] = character;
		_tones[x, y] = tone;
	}

	public Tone ToneAt(int x, int y)
	{
		return _tones[x, y];
	}

	public char CharAt(int x, int y)
	{
		return _cells[x, y];
	}

	public string Row(int y)
	{
		var row = new StringBuilder(Width);
		for (var x = 0; x < Width; x++) row.Append(_cells[x, y]);
		return row.ToString().TrimEnd();
	}

	public override string ToString()
	{
		return string.Join('\n', Enumerable.Range(0, Height).Select(Row));
	}
}