namespace Basil.Host.Cli.Commands;

public enum OptionKind : byte
{
	Text,

	Int,

	Bool,

	Mask,

	Duration,

	Path,

	List
}

public sealed record OptionDef(
	string Name,
	string Description,
	OptionKind Kind = OptionKind.Text,
	bool Required = false,
	string? Alias = null,
	bool Secret = false,
	IReadOnlyList<string>? Choices = null);

public sealed class CommandDef
{
	public required string Pattern { get; init; }

	public string? Route { get; init; }

	public IReadOnlyList<string> Routes { get; init; } = [];

	public bool Offline { get; init; }

	public bool NeedsStorage { get; init; } = true;

	public bool DashboardOnly { get; init; }

	public required string Summary { get; init; }

	public IReadOnlyList<OptionDef> Options { get; init; } = [];

	public bool OpenOptions { get; init; }

	public required Func<CommandCall, Task<int>> Run { get; init; }

	public IEnumerable<string> AllRoutes => (Route is null ? [] : new[] { Route }).Concat(Routes);

	public string[] Words => Pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);

	public static bool IsParameter(string word)
	{
		return word.StartsWith('<') && word.EndsWith('>');
	}

	public static (string Name, string Type) ParameterOf(string word)
	{
		var inner = word[1..^1];
		var colon = inner.IndexOf(':');
		return colon < 0 ? (inner, "string") : (inner[..colon], inner[(colon + 1)..]);
	}
}