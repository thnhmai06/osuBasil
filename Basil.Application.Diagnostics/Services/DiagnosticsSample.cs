namespace Basil.Application.Diagnostics.Services;

public sealed class DiagnosticsSample
{
	public static readonly IReadOnlyList<string> Categories =
	[
		"process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database", "stable", "irc",
		"events", "sessions", "connections"
	];

	private readonly Dictionary<string, object> _values;

	public DiagnosticsSample(IEnumerable<KeyValuePair<string, object>> values)
	{
		_values = new Dictionary<string, object>(values);
	}

	public IReadOnlyDictionary<string, object> All => _values;

	public object this[string category] => _values[category];

	public DiagnosticsSample Only(string category)
	{
		return new DiagnosticsSample([new KeyValuePair<string, object>(category, _values[category])]);
	}
}