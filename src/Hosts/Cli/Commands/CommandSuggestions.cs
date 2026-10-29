using System.Globalization;

namespace Basil.Host.Cli.Commands;

/// <summary>What a word of a typed command is, so the command box can colour it.</summary>
public enum TokenKind : byte
{
	/// <summary>The first word of a command.</summary>
	Command,

	/// <summary>A later word that names part of the command.</summary>
	Subcommand,

	/// <summary>A value in the command, such as an id.</summary>
	Value,

	/// <summary>An option name.</summary>
	Option,

	/// <summary>The value of an option.</summary>
	OptionValue,

	/// <summary>A word that no command accepts here.</summary>
	Unknown
}

/// <summary>A coloured stretch of a typed command line.</summary>
/// <param name="Start">The index of the first character.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Kind">What it is.</param>
public readonly record struct Token(int Start, int Length, TokenKind Kind);

/// <summary>Where the ids that complete a command come from.</summary>
public interface ISuggestionSource
{
	/// <summary>Lists the ids of something that exists, such as <c>users</c>, <c>rooms</c> or <c>channels</c>.</summary>
	/// <param name="resource">The word that precedes the id in the command.</param>
	IEnumerable<string> Ids(string resource);
}

/// <summary>The suggestions for a typed line: what could come next, and what the line already is.</summary>
public sealed class CommandSuggestions(CommandCatalog catalog, ISuggestionSource source)
{
	/// <summary>Splits a line into words with their positions; text in double quotes is one word.</summary>
	public static List<(int Start, int Length, string Text)> Words(string line)
	{
		var words = new List<(int, int, string)>();
		var i = 0;
		while (i < line.Length)
		{
			if (char.IsWhiteSpace(line[i]))
			{
				i++;
				continue;
			}

			var start = i;
			var quoted = false;
			while (i < line.Length && (quoted || !char.IsWhiteSpace(line[i])))
			{
				if (line[i] == '"') quoted = !quoted;
				i++;
			}

			words.Add((start, i - start, line[start..i].Trim('"')));
		}

		return words;
	}

	/// <summary>Colours a typed line.</summary>
	/// <param name="line">The line.</param>
	/// <returns>One token for every word.</returns>
	public List<Token> Highlight(string line)
	{
		var tokens = new List<Token>();
		var words = Words(line);
		var positional = new List<string>();
		var inOptions = false;
		var expectValue = false;
		foreach (var (start, length, text) in words)
		{
			if (expectValue)
			{
				tokens.Add(new Token(start, length, TokenKind.OptionValue));
				expectValue = false;
			}
			else if (CommandCatalog.IsOption(text))
			{
				inOptions = true;
				expectValue = !text.Contains('=');
				tokens.Add(new Token(start, length, TokenKind.Option));
			}
			else if (inOptions)
			{
				tokens.Add(new Token(start, length, TokenKind.OptionValue));
			}
			else
			{
				var candidates = catalog.Starting([.. positional, text]).ToList();
				var kind = candidates.Count == 0 ? TokenKind.Unknown
					: candidates.Any(command => PatternWord(command, positional.Count) is { } word && CommandDef.IsParameter(word) && !Literal(candidates, positional.Count, text))
						? TokenKind.Value
						: positional.Count == 0 ? TokenKind.Command : TokenKind.Subcommand;
				tokens.Add(new Token(start, length, kind));
				positional.Add(text);
			}
		}

		return tokens;
	}

	/// <summary>Lists what could be typed at the end of the line.</summary>
	/// <param name="line">The text before the caret.</param>
	/// <returns>The words that fit, the index where the word being typed starts, and its length.</returns>
	public (IReadOnlyList<string> Words, int ReplaceStart, int ReplaceLength) Suggest(string line)
	{
		var words = Words(line);
		var endsInSpace = line.Length == 0 || char.IsWhiteSpace(line[^1]);
		var partial = endsInSpace ? string.Empty : words[^1].Text;
		var start = endsInSpace ? line.Length : words[^1].Start;
		var done = (endsInSpace ? words : words.Take(words.Count - 1)).Select(word => word.Text).ToList();
		var positional = done.TakeWhile(word => !CommandCatalog.IsOption(word)).ToList();
		var typedOptions = done.Skip(positional.Count).ToList();
		var result = new List<string>();

		if (typedOptions.Count == 0 && !partial.StartsWith('-')) AddWordSuggestions(result, positional, partial);

		var exact = catalog.All.FirstOrDefault(command => Fits(command, positional));
		if (exact is not null && (partial.StartsWith('-') || partial.Length == 0))
			AddOptionSuggestions(result, exact, typedOptions, partial);

		return ([.. result.Distinct()], start, partial.Length);
	}

	private void AddWordSuggestions(List<string> result, List<string> positional, string partial)
	{
		foreach (var command in catalog.Starting(positional).Where(command => command.Words.Length > positional.Count))
		{
			var word = command.Words[positional.Count];
			if (CommandDef.IsParameter(word))
			{
				var resource = positional.Count > 0 ? positional[^1] : string.Empty;
				foreach (var id in source.Ids(resource).Where(id => id.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
					result.Add(id);
			}
			else if (word.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
			{
				result.Add(word);
			}
		}
	}

	private static void AddOptionSuggestions(List<string> result, CommandDef exact, List<string> typedOptions, string partial)
	{
		var given = typedOptions.Where(CommandCatalog.IsOption).Select(option => option.TrimStart('-')).ToHashSet(StringComparer.OrdinalIgnoreCase);
		result.AddRange(exact.Options.Where(option => !given.Contains(option.Name) && $"--{option.Name}".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
			.Select(option => $"--{option.Name}"));
	}

	/// <summary>Describes the command a line is heading for: <c>pattern  summary</c>.</summary>
	/// <param name="line">The line.</param>
	/// <returns>The description, or an empty string when no command fits.</returns>
	public string Describe(string line)
	{
		var positional = Words(line).Select(word => word.Text).TakeWhile(word => !CommandCatalog.IsOption(word)).ToList();
		var command = catalog.All.FirstOrDefault(c => Fits(c, positional)) ?? catalog.Starting(positional).FirstOrDefault();
		return command is null || positional.Count == 0 ? string.Empty : $"{command.Pattern}  {command.Summary}";
	}

	private static string? PatternWord(CommandDef command, int index)
	{
		return index < command.Words.Length ? command.Words[index] : null;
	}

	private static bool Literal(List<CommandDef> candidates, int index, string text)
	{
		return candidates.Any(command => PatternWord(command, index) is { } word && !CommandDef.IsParameter(word) &&
		                                 word.StartsWith(text, StringComparison.OrdinalIgnoreCase));
	}

	private static bool Fits(CommandDef command, IReadOnlyList<string> words)
	{
		var pattern = command.Words;
		if (pattern.Length != words.Count) return false;

		for (var i = 0; i < pattern.Length; i++)
		{
			if (CommandDef.IsParameter(pattern[i]))
			{
				var type = CommandDef.ParameterOf(pattern[i]).Type;
				if (type == "int" && !int.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
				if (type == "guid" && !Guid.TryParse(words[i], out _)) return false;
			}
			else if (!pattern[i].Equals(words[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}

		return true;
	}
}
