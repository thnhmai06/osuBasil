namespace Basil.Application.Configuration;

/// <summary>
///     SQLite database file location. Relative paths are anchored to the executable's directory
///     (not the process's working directory) by Infrastructure's connection-string builder — this
///     is a plain data holder only.
///     Always fixed to Data/Basil.db under the executable directory.
/// </summary>
public sealed class DatabaseOptions
{
    public string Path { get; init; } = System.IO.Path.Combine("Data", "Basil.db");
}
