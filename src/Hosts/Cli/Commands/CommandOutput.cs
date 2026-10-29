namespace Basil.Host.Cli.Commands;

/// <summary>What a line of command output is, so the dashboard can colour it and a shell prints it plain.</summary>
public enum OutputKind : byte
{
	/// <summary>Ordinary text.</summary>
	Text,

	/// <summary>A <c>label: value</c> line.</summary>
	Field,

	/// <summary>The header row of a table.</summary>
	Header,

	/// <summary>A table row.</summary>
	Row,

	/// <summary>The confirmation of an action.</summary>
	Ok,

	/// <summary>A refusal or an error.</summary>
	Error
}

/// <summary>One line of command output.</summary>
/// <param name="Kind">What the line is.</param>
/// <param name="Text">The whole line as plain text.</param>
public readonly record struct OutputLine(OutputKind Kind, string Text);

/// <summary>Receives what a command prints, one line at a time.</summary>
public interface ICommandOutput
{
	/// <summary>Writes one line.</summary>
	/// <param name="line">The line.</param>
	void Write(OutputLine line);
}

/// <summary>Writes the lines of a command as plain text to a <see cref="TextWriter" />, without colour.</summary>
/// <param name="writer">Where the text goes.</param>
public sealed class PlainOutput(TextWriter writer) : ICommandOutput
{
	/// <inheritdoc />
	public void Write(OutputLine line)
	{
		writer.WriteLine(line.Text);
	}
}

/// <summary>Collects the lines of a command, for tests and for the dashboard's history.</summary>
public sealed class CollectedOutput : ICommandOutput
{
	private readonly List<OutputLine> _lines = [];

	/// <summary>Gets the lines written so far.</summary>
	public IReadOnlyList<OutputLine> Lines => _lines;

	/// <summary>Gets the lines as plain text, joined by new lines.</summary>
	public string Text => string.Join('\n', _lines.Select(line => line.Text));

	/// <inheritdoc />
	public void Write(OutputLine line)
	{
		_lines.Add(line);
	}
}

/// <summary>Helpers that print the shapes a command shows: confirmations, errors, aligned fields and tables.</summary>
public static class OutputExtensions
{
	/// <summary>Writes an ordinary line.</summary>
	public static void Line(this ICommandOutput output, string text)
	{
		output.Write(new OutputLine(OutputKind.Text, text));
	}

	/// <summary>Writes the confirmation of an action.</summary>
	public static void Ok(this ICommandOutput output, string text)
	{
		output.Write(new OutputLine(OutputKind.Ok, text));
	}

	/// <summary>Writes a refusal as <c>error: code: message</c>.</summary>
	public static void Error(this ICommandOutput output, string code, string message)
	{
		output.Write(new OutputLine(OutputKind.Error, $"error: {code}: {message}"));
	}

	/// <summary>Writes labelled values with the labels padded to one column.</summary>
	public static void Fields(this ICommandOutput output, IReadOnlyList<(string Label, string Value)> fields, string indent = "")
	{
		var width = fields.Count == 0 ? 0 : fields.Max(field => field.Label.Length);
		foreach (var (label, value) in fields)
			output.Write(new OutputLine(OutputKind.Field, $"{indent}{(label + ":").PadRight(width + 1)} {value}".TrimEnd()));
	}

	/// <summary>Writes a table with a header row, each column as wide as its widest cell.</summary>
	public static void Table(this ICommandOutput output, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows,
		string indent = "")
	{
		var widths = header.Select((title, index) => Math.Max(title.Length,
			rows.Count == 0 ? 0 : rows.Max(row => row[index].Length))).ToArray();

		string Format(IReadOnlyList<string> cells)
		{
			return indent + string.Join("  ", cells.Select((cell, index) => cell.PadRight(widths[index]))).TrimEnd();
		}

		output.Write(new OutputLine(OutputKind.Header, Format(header)));
		foreach (var row in rows) output.Write(new OutputLine(OutputKind.Row, Format(row)));
	}
}
