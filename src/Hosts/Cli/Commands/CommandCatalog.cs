using System.Globalization;
using System.Text;

namespace Basil.Host.Cli.Commands;

/// <summary>A command line that was matched with a command.</summary>
/// <param name="Command">The command.</param>
/// <param name="Words">The values of the parameter words, by name.</param>
/// <param name="Options">The options that were given, by name.</param>
public sealed record ParsedCommand(
	CommandDef Command,
	Dictionary<string, string> Words,
	Dictionary<string, string> Options);

/// <summary>Why a command line could not be understood.</summary>
/// <param name="Message">What is wrong.</param>
/// <param name="Candidates">The commands the line may have meant.</param>
public sealed record ParseFailure(string Message, IReadOnlyList<CommandDef> Candidates);

/// <summary>The set of console commands, and the parser that matches a command line with one of them.</summary>
public sealed class CommandCatalog
{
	/// <summary>Creates a catalog.</summary>
	/// <param name="commands">The commands.</param>
	public CommandCatalog(IEnumerable<CommandDef> commands)
	{
		All = [.. commands];
	}

	/// <summary>Gets every command.</summary>
	public IReadOnlyList<CommandDef> All { get; }

	/// <summary>Splits a typed line into words; text in double quotes is one word and a backslash escapes the next character.</summary>
	/// <param name="line">The line.</param>
	/// <returns>The words.</returns>
	public static List<string> Split(string line)
	{
		var words = new List<string>();
		var current = new StringBuilder();
		var inWord = false;
		var quoted = false;
		for (var i = 0; i < line.Length; i++)
		{
			var c = line[i];
			if (c == '\\' && i + 1 < line.Length && (quoted || line[i + 1] is '"' or '\\'))
			{
				current.Append(line[++i]);
				inWord = true;
			}
			else if (c == '"')
			{
				quoted = !quoted;
				inWord = true;
			}
			else if (char.IsWhiteSpace(c) && !quoted)
			{
				if (!inWord) continue;

				words.Add(current.ToString());
				current.Clear();
				inWord = false;
			}
			else
			{
				current.Append(c);
				inWord = true;
			}
		}

		if (inWord) words.Add(current.ToString());
		return words;
	}

	/// <summary>Splits the arguments into the words that name the command and the options that follow.</summary>
	public static (List<string> Words, List<string> Options) Separate(IReadOnlyList<string> args)
	{
		var index = 0;
		var words = new List<string>();
		while (index < args.Count && !IsOption(args[index])) words.Add(args[index++]);
		return (words, [.. args.Skip(index)]);
	}

	/// <summary>Tells whether a word is an option and not a value.</summary>
	public static bool IsOption(string word)
	{
		return word.Length > 1 && word[0] == '-' &&
		       !double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
	}

	/// <summary>Finds the commands whose words start with the given words.</summary>
	/// <param name="words">The words typed so far.</param>
	public IEnumerable<CommandDef> Starting(IReadOnlyList<string> words)
	{
		return All.Where(command => Matches(command.Words, words, false) > 0 || words.Count == 0);
	}

	/// <summary>Matches a command line with a command.</summary>
	/// <param name="args">The words of the line.</param>
	/// <returns>The matched command, or why none matched.</returns>
	public (ParsedCommand? Parsed, ParseFailure? Failure) Parse(IReadOnlyList<string> args)
	{
		var (words, optionTokens) = Separate(args);
		if (words.Count == 0) return (null, new ParseFailure("No command given.", []));

		var best = All.Select(command => (Command: command, Score: Matches(command.Words, words, true)))
			.Where(entry => entry.Score > 0).OrderByDescending(entry => entry.Score).FirstOrDefault().Command;
		if (best is null)
		{
			var candidates = All.Where(command => command.Words[0].Equals(words[0], StringComparison.OrdinalIgnoreCase))
				.ToList();
			return (null, new ParseFailure(candidates.Count == 0
				? $"Unknown command: {words[0]}. Type help for the list."
				: $"Unknown command: {string.Join(' ', words)}. Type help {words[0]} for its commands.", candidates));
		}

		var parameters = new Dictionary<string, string>();
		for (var i = 0; i < best.Words.Length; i++)
			if (CommandDef.IsParameter(best.Words[i]))
				parameters[CommandDef.ParameterOf(best.Words[i]).Name] = words[i];

		var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < optionTokens.Count; i++)
		{
			var token = optionTokens[i];
			if (!IsOption(token)) return (null, new ParseFailure($"Unexpected value: {token}.", [best]));

			var name = token.TrimStart('-');
			string? value = null;
			var equals = name.IndexOf('=');
			if (equals >= 0)
			{
				value = name[(equals + 1)..];
				name = name[..equals];
			}

			var option = best.Options.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
			                                              (o.Alias is not null && !token.StartsWith("--") &&
			                                               o.Alias.Equals(name, StringComparison.Ordinal)));
			if (option is null && !best.OpenOptions)
				return (null, new ParseFailure($"Unknown option: {token}.", [best]));

			var key = option?.Name ?? name;
			if (value is null)
			{
				var next = i + 1 < optionTokens.Count ? optionTokens[i + 1] : null;
				if (option?.Kind == OptionKind.Bool)
				{
					if (next is not null && next.ToLowerInvariant() is "true" or "false") i++;
					value = next is not null && next.ToLowerInvariant() is "true" or "false" ? next : "true";
				}
				else if (next is null || (IsOption(next) && option?.Kind is not OptionKind.Int))
				{
					return (null, new ParseFailure($"{token} needs a value.", [best]));
				}
				else
				{
					value = next;
					i++;
				}
			}

			options[key] = value;
		}

		foreach (var required in best.Options.Where(o => o.Required && !options.ContainsKey(o.Name)))
			return (null, new ParseFailure($"--{required.Name} is required.", [best]));

		return (new ParsedCommand(best, parameters, options), null);
	}

	/// <summary>Scores how well typed words match a pattern: the number of literal words that matched, or 0 for no match.</summary>
	private static int Matches(string[] pattern, IReadOnlyList<string> words, bool exact)
	{
		if (exact ? pattern.Length != words.Count : words.Count > pattern.Length) return 0;

		var literals = 0;
		for (var i = 0; i < words.Count; i++)
			if (CommandDef.IsParameter(pattern[i]))
			{
				var type = CommandDef.ParameterOf(pattern[i]).Type;
				var fits = type switch
				{
					"int" => int.TryParse(words[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
					"guid" => Guid.TryParse(words[i], out _),
					_ => true
				};
				if (!fits) return 0;
			}
			else if (pattern[i].Equals(words[i], StringComparison.OrdinalIgnoreCase))
			{
				literals++;
			}
			else
			{
				return 0;
			}

		return literals + (exact ? 1 : 0) + (words.Count == 0 ? 0 : 0);
	}
}