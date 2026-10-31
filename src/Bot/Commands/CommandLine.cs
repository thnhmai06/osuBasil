using System.Text;

namespace Basil.Bot.Commands;

/// <summary>The operator that joins two command segments in a chain.</summary>
internal enum ChainOperator : byte
{
	/// <summary>The second segment always runs.</summary>
	Semicolon,

	/// <summary>The second segment runs only when the first succeeds.</summary>
	And
}

/// <summary>One segment of a chained command message.</summary>
/// <param name="Text">The command text, without its leading operator.</param>
/// <param name="Operator">How this segment connects to the one before it.</param>
internal sealed record CommandSegment(string Text, ChainOperator Operator);

/// <summary>Splits a command message into segments and tokenises each segment.</summary>
/// <remarks>
///     Both steps read the same syntax: a double quote starts or ends a quoted string, and a backslash before a double
///     quote or another backslash makes that character an ordinary one.
/// </remarks>
internal static class CommandLine
{
	/// <summary>Splits a command message into segments, respecting quoted strings and escape sequences.</summary>
	/// <param name="message">The full command message, prefix included.</param>
	/// <returns>The segments, in order; the first carries <see cref="ChainOperator.Semicolon" />.</returns>
	public static IReadOnlyList<CommandSegment> Split(string message)
	{
		var segments = new List<CommandSegment>();
		var current = new StringBuilder();
		var op = ChainOperator.Semicolon;
		var inQuote = false;

		for (var i = 0; i < message.Length; i++)
		{
			var c = message[i];

			if (IsEscape(message, i))
			{
				current.Append(c).Append(message[++i]);
				continue;
			}

			if (c == '"') inQuote = !inQuote;

			if (!inQuote && c == ';')
			{
				FlushSegment(ChainOperator.Semicolon);
			}
			else if (!inQuote && c == '&' && i + 1 < message.Length && message[i + 1] == '&')
			{
				FlushSegment(ChainOperator.And);
				i++;
			}
			else
			{
				current.Append(c);
			}
		}

		var last = current.ToString().Trim();
		if (last.Length > 0)
			segments.Add(new CommandSegment(last, op));

		return segments.Count > 0 ? segments : [new CommandSegment(message.Trim(), ChainOperator.Semicolon)];

		void FlushSegment(ChainOperator nextOp)
		{
			var text = current.ToString().Trim();
			if (text.Length > 0)
				segments.Add(new CommandSegment(text, op));
			current.Clear();
			op = nextOp;
		}
	}

	/// <summary>Tokenises a command segment into words, respecting quoted strings and escape sequences.</summary>
	/// <param name="segment">The command text.</param>
	/// <returns>The words, with quotes stripped and escapes resolved.</returns>
	public static IReadOnlyList<string> Tokenize(string segment)
	{
		var tokens = new List<string>();
		var current = new StringBuilder();
		var inQuote = false;

		for (var i = 0; i < segment.Length; i++)
		{
			var c = segment[i];

			if (IsEscape(segment, i))
			{
				current.Append(segment[++i]);
			}
			else if (c == '"')
			{
				inQuote = !inQuote;
			}
			else if (c == ' ' && !inQuote)
			{
				if (current.Length == 0) continue;
				tokens.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}

		if (current.Length > 0)
			tokens.Add(current.ToString());

		return tokens;
	}

	private static bool IsEscape(string text, int index)
	{
		return text[index] == '\\' && index + 1 < text.Length && text[index + 1] is '"' or '\\';
	}
}