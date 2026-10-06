using System.Text;

namespace Basil.Bot.Application.Commands;

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
internal static class CommandLine
{
	/// <summary>Splits a command message into segments, respecting quoted strings and escape sequences.</summary>
	/// <param name="message">The full command message, prefix included.</param>
	/// <returns>The segments, in order; the first carries <see cref="ChainOperator.Semicolon" />.</returns>
	public static IReadOnlyList<CommandSegment> Split(string message)
	{
		var segments = new List<CommandSegment>();
		var current = new StringBuilder();
		var i = 0;
		var op = ChainOperator.Semicolon;
		var inQuote = false;

		void FlushSegment(ChainOperator nextOp)
		{
			var text = current.ToString().Trim();
			if (text.Length > 0)
				segments.Add(new CommandSegment(text, op));
			current.Clear();
			op = nextOp;
		}

		while (i < message.Length)
		{
			var c = message[i];

			if (inQuote)
			{
				if (c == '\\' && i + 1 < message.Length)
				{
					current.Append(c);
					current.Append(message[i + 1]);
					i += 2;
					continue;
				}

				if (c == '"')
				{
					current.Append(c);
					inQuote = false;
					i++;
					continue;
				}

				current.Append(c);
				i++;
				continue;
			}

			if (c == '"')
			{
				current.Append(c);
				inQuote = true;
				i++;
				continue;
			}

			if (c == ';' && !inQuote)
			{
				FlushSegment(ChainOperator.Semicolon);
				i++;
				continue;
			}

			if (c == '&' && i + 1 < message.Length && message[i + 1] == '&')
			{
				FlushSegment(ChainOperator.And);
				i += 2;
				continue;
			}

			current.Append(c);
			i++;
		}

		// Flush the last segment
		var last = current.ToString().Trim();
		if (last.Length > 0)
			segments.Add(new CommandSegment(last, op));

		return segments.Count > 0 ? segments : [new CommandSegment(message.Trim(), ChainOperator.Semicolon)];
	}

	/// <summary>Tokenises a command segment into words, respecting quoted strings and escape sequences.</summary>
	/// <param name="segment">The command text.</param>
	/// <returns>The words, with quotes stripped and escapes resolved.</returns>
	public static IReadOnlyList<string> Tokenize(string segment)
	{
		var tokens = new List<string>();
		var current = new StringBuilder();
		var i = 0;
		var inQuote = false;

		while (i < segment.Length)
		{
			var c = segment[i];

			if (inQuote)
			{
				if (c == '\\' && i + 1 < segment.Length)
				{
					var next = segment[i + 1];
					if (next is '"' or '\\')
					{
						current.Append(next);
						i += 2;
						continue;
					}
				}

				if (c == '"')
				{
					inQuote = false;
					i++;
					continue;
				}

				current.Append(c);
				i++;
				continue;
			}

			if (c == '"')
			{
				inQuote = true;
				i++;
				continue;
			}

			if (c == ' ')
			{
				if (current.Length > 0)
				{
					tokens.Add(current.ToString());
					current.Clear();
				}
				i++;
				continue;
			}

			if (c == '\\' && i + 1 < segment.Length)
			{
				var next = segment[i + 1];
				if (next is '"' or '\\')
				{
					current.Append(next);
					i += 2;
					continue;
				}
			}

			current.Append(c);
			i++;
		}

		if (current.Length > 0)
			tokens.Add(current.ToString());

		return tokens;
	}
}
