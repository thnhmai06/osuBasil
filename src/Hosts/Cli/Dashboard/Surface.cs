using System.Text;

namespace Basil.Host.Cli.Dashboard;

/// <summary>What a piece of the dashboard means; the theme decides the colour.</summary>
public enum Tone : byte
{
	/// <summary>Ordinary text.</summary>
	Text,

	/// <summary>Less important text, borders and labels.</summary>
	Muted,

	/// <summary>Titles and the selected tab.</summary>
	Accent,

	/// <summary>Good: success, low values.</summary>
	Good,

	/// <summary>A warning.</summary>
	Warn,

	/// <summary>An error.</summary>
	Bad,

	/// <summary>The selected row.</summary>
	Selected,

	/// <summary>The cell of a graph; the heat says how high the value is.</summary>
	Heat
}

/// <summary>A rectangle of the surface, in cells.</summary>
/// <param name="X">The left column.</param>
/// <param name="Y">The top row.</param>
/// <param name="Width">The number of columns.</param>
/// <param name="Height">The number of rows.</param>
public readonly record struct Area(int X, int Y, int Width, int Height)
{
	/// <summary>Gets the column after the last one.</summary>
	public int Right => X + Width;

	/// <summary>Gets the row after the last one.</summary>
	public int Bottom => Y + Height;

	/// <summary>Takes rows off the top.</summary>
	public Area Top(int rows)
	{
		return this with { Height = Math.Min(rows, Height) };
	}

	/// <summary>Leaves the area below some rows.</summary>
	public Area Below(int rows)
	{
		return new Area(X, Y + Math.Min(rows, Height), Width, Math.Max(Height - rows, 0));
	}

	/// <summary>Takes columns off the left.</summary>
	public Area Left(int columns)
	{
		return this with { Width = Math.Min(columns, Width) };
	}

	/// <summary>Leaves the area right of some columns.</summary>
	public Area After(int columns)
	{
		return new Area(X + Math.Min(columns, Width), Y, Math.Max(Width - columns, 0), Height);
	}
}

/// <summary>Where the dashboard draws: a grid of cells, each with a character and a tone.</summary>
public interface IPaintSurface
{
	/// <summary>Gets the number of columns.</summary>
	int Width { get; }

	/// <summary>Gets the number of rows.</summary>
	int Height { get; }

	/// <summary>Puts one character in a cell; cells outside the surface are ignored.</summary>
	/// <param name="x">The column.</param>
	/// <param name="y">The row.</param>
	/// <param name="character">The character (a braille cell is one character too).</param>
	/// <param name="tone">What it means.</param>
	/// <param name="heat">For <see cref="Tone.Heat" />: how high the value is, from 0 to 1.</param>
	void Put(int x, int y, char character, Tone tone, double heat = 0);
}

/// <summary>Drawing helpers on a surface.</summary>
public static class SurfaceExtensions
{
	/// <summary>Writes text from a cell, cut at the edge of the area.</summary>
	/// <returns>The column after the last character written.</returns>
	public static int Text(this IPaintSurface surface, Area area, int column, int row, string text, Tone tone = Tone.Text)
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

	/// <summary>Fills a row of the area with one character.</summary>
	public static void Rule(this IPaintSurface surface, Area area, int row, char character, Tone tone = Tone.Muted)
	{
		for (var x = 0; x < area.Width; x++) surface.Put(area.X + x, area.Y + row, character, tone);
	}

	/// <summary>Writes a panel title in its first row.</summary>
	public static void Title(this IPaintSurface surface, Area area, string title)
	{
		surface.Text(area, 0, 0, title, Tone.Accent);
	}

	/// <summary>Cuts text to a width, ending with an ellipsis when it is longer.</summary>
	public static string Fit(this string text, int width)
	{
		return width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";
	}
}

/// <summary>A surface kept in memory, for tests and for turning a painted panel into text.</summary>
public sealed class GridSurface : IPaintSurface
{
	private readonly char[,] _cells;
	private readonly Tone[,] _tones;

	/// <summary>Creates a blank grid.</summary>
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

	/// <inheritdoc />
	public int Width { get; }

	/// <inheritdoc />
	public int Height { get; }

	/// <inheritdoc />
	public void Put(int x, int y, char character, Tone tone, double heat = 0)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height) return;

		_cells[x, y] = character;
		_tones[x, y] = tone;
	}

	/// <summary>Gets the tone of a cell.</summary>
	public Tone ToneAt(int x, int y)
	{
		return _tones[x, y];
	}

	/// <summary>Gets the character of a cell.</summary>
	public char CharAt(int x, int y)
	{
		return _cells[x, y];
	}

	/// <summary>Gets one row as text.</summary>
	public string Row(int y)
	{
		var row = new StringBuilder(Width);
		for (var x = 0; x < Width; x++) row.Append(_cells[x, y]);
		return row.ToString().TrimEnd();
	}

	/// <summary>Gets the whole grid as text, one row per line.</summary>
	public override string ToString()
	{
		return string.Join('\n', Enumerable.Range(0, Height).Select(Row));
	}
}
