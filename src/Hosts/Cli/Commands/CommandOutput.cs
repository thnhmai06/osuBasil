namespace Basil.Host.Cli.Commands;

public enum OutputKind : byte
{
	Text,

	Field,

	Header,

	Row,

	Ok,

	Error
}

public readonly record struct OutputLine(OutputKind Kind, string Text);

public interface ICommandOutput
{
	void Write(OutputLine line);
}

public sealed class PlainOutput(TextWriter writer) : ICommandOutput
{
	public void Write(OutputLine line)
	{
		writer.WriteLine(line.Text);
	}
}

public sealed class CollectedOutput : ICommandOutput
{
	private readonly List<OutputLine> _lines = [];

	public IReadOnlyList<OutputLine> Lines => _lines;

	public string Text => string.Join('\n', _lines.Select(line => line.Text));

	public void Write(OutputLine line)
	{
		_lines.Add(line);
	}
}

public static class OutputExtensions
{
	public static void Line(this ICommandOutput output, string text)
	{
		output.Write(new OutputLine(OutputKind.Text, text));
	}

	public static void Ok(this ICommandOutput output, string text)
	{
		output.Write(new OutputLine(OutputKind.Ok, text));
	}

	public static void Error(this ICommandOutput output, string code, string message)
	{
		output.Write(new OutputLine(OutputKind.Error, $"error: {code}: {message}"));
	}

	public static void Fields(this ICommandOutput output, IReadOnlyList<(string Label, string Value)> fields,
		string indent = "")
	{
		var width = fields.Count == 0 ? 0 : fields.Max(field => field.Label.Length);
		foreach (var (label, value) in fields)
			output.Write(new OutputLine(OutputKind.Field,
				$"{indent}{(label + ":").PadRight(width + 1)} {value}".TrimEnd()));
	}

	public static void Table(this ICommandOutput output, IReadOnlyList<string> header,
		IReadOnlyList<IReadOnlyList<string>> rows,
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