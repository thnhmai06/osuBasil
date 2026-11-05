namespace Basil.Application.Common.Diagnostics;

/// <summary>
///     The measurements of every diagnostics category at one moment, which the HTTP API streams and the dashboard
///     draws; it never changes after it is taken.
/// </summary>
public sealed class DiagnosticsSample
{
	/// <summary>The names of the categories, in the order the API lists them.</summary>
	public static readonly IReadOnlyList<string> Categories =
	[
		"process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database", "stable", "irc",
		"events", "sessions"
	];

	private readonly Dictionary<string, object> _values;

	/// <summary>Creates a sample.</summary>
	/// <param name="values">The value of each category, by category name.</param>
	public DiagnosticsSample(IEnumerable<KeyValuePair<string, object>> values)
	{
		_values = new Dictionary<string, object>(values);
	}

	/// <summary>Gets the value of each category, by category name.</summary>
	public IReadOnlyDictionary<string, object> All => _values;

	/// <summary>Gets the value of one category.</summary>
	/// <param name="category">The name of the category.</param>
	/// <exception cref="KeyNotFoundException">The sample has no such category.</exception>
	public object this[string category] => _values[category];

	/// <summary>Makes a sample of the one category.</summary>
	/// <param name="category">The name of the category.</param>
	/// <returns>A sample that holds only that category.</returns>
	public DiagnosticsSample Only(string category)
	{
		return new DiagnosticsSample([new KeyValuePair<string, object>(category, _values[category])]);
	}
}