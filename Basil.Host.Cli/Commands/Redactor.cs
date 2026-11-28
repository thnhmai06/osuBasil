namespace Basil.Host.Cli.Commands;

public static class Redactor
{
	public const string Mask = "***";

	private static readonly string[] AlwaysSecret = ["password", "new", "current", "key"];

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

	public static string Redact(CommandCatalog catalog, string line)
	{
		return Redact(catalog, CommandCatalog.Split(line));
	}

	private static string Quote(string word)
	{
		return word.Length == 0 || word.Any(char.IsWhiteSpace) ? $"\"{word.Replace("\"", "\\\"")}\"" : word;
	}
}