using System.Globalization;

namespace Basil.Host.Cli.Commands;

public enum TokenKind : byte
{
	Command,

	Subcommand,

	Value,

	Option,

	OptionValue,

	Unknown
}

public readonly record struct Token(int Start, int Length, TokenKind Kind);

public interface ISuggestionSource
{
	IEnumerable<string> Ids(string resource);
}

public sealed class CommandSuggestions(CommandCatalog catalog, ISuggestionSource source)
{
	public static IReadOnlyList<(int Start, int Length, string Text)> Words(string line)
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

	public IReadOnlyList<Token> Highlight(string line)
	{
		var tokens = new List<Token>();
		var words = Words(line);
		var positional = new List<string>();
		var inOptions = false;
		var expectValue = false;
		foreach (var (start, length, text) in words)
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
				var kind = candidates.Count == 0
					? TokenKind.Unknown
					: candidates.Any(command =>
						PatternWord(command, positional.Count) is { } word && CommandDef.IsParameter(word) &&
						!Literal(candidates, positional.Count, text))
						? TokenKind.Value
						: positional.Count == 0
							? TokenKind.Command
							: TokenKind.Subcommand;
				tokens.Add(new Token(start, length, kind));
				positional.Add(text);
			}

		return tokens;
	}

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

	public (IReadOnlyList<(string Word, string Description)> Items, int ReplaceStart, int ReplaceLength) SuggestDescribed(
		string line)
	{
		var (words, start, length) = Suggest(line);
		var positional = Words(line[..start]).Select(word => word.Text).TakeWhile(word => !CommandCatalog.IsOption(word))
			.ToList();
		return ([.. words.Select(word => (word, DescribeWord(positional, word)))], start, length);
	}

	private string DescribeWord(List<string> positional, string word)
	{
		if (word.StartsWith('-'))
			return catalog.All.FirstOrDefault(command => Fits(command, positional))?.Options
				.FirstOrDefault(option => $"--{option.Name}" == word)?.Description ?? string.Empty;

		if (CommandDef.IsParameter(word))
			return catalog.Starting(positional).FirstOrDefault(command =>
				command.Words.Length == positional.Count + 1 && command.Words[positional.Count] == word)?.Summary ?? string.Empty;

		var typed = new List<string>(positional) { word };
		var candidates = catalog.Starting(typed).ToList();
		return candidates.FirstOrDefault(command => Fits(command, typed))?.Summary ??
		       (candidates.Count > 1 ? $"{candidates.Count} commands" : candidates.FirstOrDefault()?.Summary ?? string.Empty);
	}

	private void AddWordSuggestions(List<string> result, List<string> positional, string partial)
	{
		foreach (var command in catalog.Starting(positional).Where(command => command.Words.Length > positional.Count))
		{
			var word = command.Words[positional.Count];
			if (CommandDef.IsParameter(word) && partial.Length == 0)
			{
				// Nothing typed yet: name the parameter instead of guessing a value.
				result.Add(word);
			}
			else if (CommandDef.IsParameter(word))
			{
				var resource = positional.Count > 0 ? positional[^1] : string.Empty;
				foreach (var id in source.Ids(resource)
					         .Where(id => id.StartsWith(partial, StringComparison.OrdinalIgnoreCase)))
					result.Add(id);
			}
			else if (word.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
			{
				result.Add(word);
			}
		}
	}

	private static void AddOptionSuggestions(List<string> result, CommandDef exact, List<string> typedOptions,
		string partial)
	{
		var given = typedOptions.Where(CommandCatalog.IsOption).Select(option => option.TrimStart('-'))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		result.AddRange(exact.Options
			.Where(option => !given.Contains(option.Name) &&
			                 $"--{option.Name}".StartsWith(partial, StringComparison.OrdinalIgnoreCase))
			.Select(option => $"--{option.Name}"));
	}

	public string Describe(string line)
	{
		var positional = Words(line).Select(word => word.Text).TakeWhile(word => !CommandCatalog.IsOption(word))
			.ToList();
		var command = catalog.All.FirstOrDefault(c => Fits(c, positional)) ??
		              catalog.Starting(positional).FirstOrDefault();
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
			if (CommandDef.IsParameter(pattern[i]))
			{
				var type = CommandDef.ParameterOf(pattern[i]).Type;
				if (type == "int" && !int.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
					return false;
				if (type == "long" &&
				    !long.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
					return false;
			}
			else if (!pattern[i].Equals(words[i], StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

		return true;
	}
}