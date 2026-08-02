namespace Basil.Application.Configurations;

/// <summary>
///     Configuration for the SQLite database file location.
/// </summary>
/// <remarks>
///     Relative paths are anchored to the executable's directory, not the process's working
///     directory, by Infrastructure's connection-string builder; this class is a plain data holder
///     and performs no resolution itself. The default value resolves to <c>Data/Basil.db</c> under
///     the executable directory.
/// </remarks>
public sealed class DatabaseOptions
{
	/// <summary>Gets or sets the SQLite database file path, relative to the executable's directory.</summary>
	/// <value>Defaults to <c>Data/Basil.db</c>.</value>
	public string Path { get; init; } = System.IO.Path.Combine("Data", "Basil.db");
}