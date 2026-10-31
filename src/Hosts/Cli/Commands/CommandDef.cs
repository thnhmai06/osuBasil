namespace Basil.Host.Cli.Commands;

/// <summary>How the value of an option is read.</summary>
public enum OptionKind : byte
{
	/// <summary>Any text.</summary>
	Text,

	/// <summary>A whole number.</summary>
	Int,

	/// <summary>A boolean: <c>true</c>, <c>false</c>, or the bare option meaning <c>true</c>.</summary>
	Bool,

	/// <summary>A bitmask of flags, as a number.</summary>
	Mask,

	/// <summary>A length of time such as <c>30m</c>, <c>12h</c> or <c>7d</c>.</summary>
	Duration,

	/// <summary>A path of a file on the machine.</summary>
	Path,

	/// <summary>A list of values separated by commas.</summary>
	List
}

/// <summary>One option of a command; its name is the query parameter or body field of the API route it mirrors.</summary>
/// <param name="Name">The camelCase name, written <c>--name</c>.</param>
/// <param name="Description">What the option does.</param>
/// <param name="Kind">How the value is read.</param>
/// <param name="Required">Whether the command cannot run without it.</param>
/// <param name="Alias">An optional one-letter short name.</param>
/// <param name="Secret">Whether the value is a password or a key, masked wherever the command is recorded.</param>
public sealed record OptionDef(
	string Name,
	string Description,
	OptionKind Kind = OptionKind.Text,
	bool Required = false,
	string? Alias = null,
	bool Secret = false);

/// <summary>
///     A console command: where it is typed, which API route it mirrors, whether it needs the server, and what it
///     does.
/// </summary>
public sealed class CommandDef
{
	/// <summary>
	///     Gets the words of the command; <c>&lt;id:int&gt;</c> stands for a value, such as
	///     <c>users &lt;id:int&gt; set</c>.
	/// </summary>
	public required string Pattern { get; init; }

	/// <summary>
	///     Gets the HTTP route this command mirrors (<c>GET /v1/users</c>), or <see langword="null" /> for a console-only
	///     command.
	/// </summary>
	public string? Route { get; init; }

	/// <summary>Gets additional routes the same command covers.</summary>
	public IReadOnlyList<string> Routes { get; init; } = [];

	/// <summary>Gets a value that indicates whether the command runs with the server stopped (the P column).</summary>
	public bool Offline { get; init; }

	/// <summary>
	///     Gets a value that indicates whether running the command offline needs the storage started (<c>help</c> does
	///     not).
	/// </summary>
	public bool NeedsStorage { get; init; } = true;

	/// <summary>Gets a value that indicates whether the command only makes sense in the dashboard.</summary>
	public bool DashboardOnly { get; init; }

	/// <summary>Gets one sentence that says what the command does.</summary>
	public required string Summary { get; init; }

	/// <summary>Gets the options of the command.</summary>
	public IReadOnlyList<OptionDef> Options { get; init; } = [];

	/// <summary>
	///     Gets a value that indicates whether the command takes any <c>--field.path value</c> besides its listed
	///     options.
	/// </summary>
	public bool OpenOptions { get; init; }

	/// <summary>Gets what the command does.</summary>
	public required Func<CommandCall, Task<int>> Run { get; init; }

	/// <summary>Gets every route the command covers.</summary>
	public IEnumerable<string> AllRoutes => (Route is null ? [] : new[] { Route }).Concat(Routes);

	/// <summary>Gets the words of <see cref="Pattern" />.</summary>
	public string[] Words => Pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>Tells whether a word of the pattern stands for a value.</summary>
	public static bool IsParameter(string word)
	{
		return word.StartsWith('<') && word.EndsWith('>');
	}

	/// <summary>Reads the name and type of a parameter word: <c>&lt;id:int&gt;</c> is <c>("id", "int")</c>.</summary>
	public static (string Name, string Type) ParameterOf(string word)
	{
		var inner = word[1..^1];
		var colon = inner.IndexOf(':');
		return colon < 0 ? (inner, "string") : (inner[..colon], inner[(colon + 1)..]);
	}
}