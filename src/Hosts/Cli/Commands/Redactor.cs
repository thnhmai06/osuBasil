namespace Basil.Host.Cli.Commands;

/// <summary>Hides passwords and keys wherever a command is recorded: the log, the history and the suggestions.</summary>
public static class Redactor
{
	/// <summary>The text that stands for a hidden value.</summary>
	public const string Mask = "***";

	private static readonly string[] AlwaysSecret = ["password", "new", "current", "key"];

	/// <summary>Writes the arguments of a command with the values of its secret options hidden.</summary>
	/// <param name="catalog">The commands, which say which options are secret.</param>
	/// <param name="args">The words of the command line.</param>
	/// <returns>The command line with secret values replaced.</returns>
	public static string Redact(CommandCatalog catalog, IReadOnlyList<string> args)
	{
		var (words, _) = CommandCatalog.Separate(args);
		var secrets = new HashSet<string>(AlwaysSecret, StringComparer.OrdinalIgnoreCase);
		foreach (var command in catalog.All)
		foreach (var option in command.Options.Where(option => option.Secret))
		{
			secrets.Add(option.Name);
			if (option.Alias is not null) secrets.Add(option.Alias);
		}

		var result = new List<string>();
		for (var i = 0; i < args.Count; i++)
		{
			var token = args[i];
			if (!CommandCatalog.IsOption(token) || i < words.Count)
			{
				result.Add(token);
				continue;
			}

			var name = token.TrimStart('-');
			var equals = name.IndexOf('=');
			var optionName = equals < 0 ? name : name[..equals];
			if (!secrets.Contains(optionName))
			{
				result.Add(token);
				continue;
			}

			if (equals >= 0)
			{
				result.Add($"{token[..(token.IndexOf('=') + 1)]}{Mask}");
				continue;
			}

			result.Add(token);
			if (i + 1 < args.Count)
			{
				result.Add(Mask);
				i++;
			}
		}

		return string.Join(' ', result.Select(Quote));
	}

	/// <summary>Redacts a typed line.</summary>
	public static string Redact(CommandCatalog catalog, string line)
	{
		return Redact(catalog, CommandCatalog.Split(line));
	}

	private static string Quote(string word)
	{
		return word.Length == 0 || word.Any(char.IsWhiteSpace) ? $"\"{word.Replace("\"", "\\\"")}\"" : word;
	}
}
