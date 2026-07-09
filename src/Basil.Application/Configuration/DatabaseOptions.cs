namespace Basil.Application.Configuration;

/// <summary>
///     SQLite database file location. Relative paths are anchored to the executable's directory
///     (not the process's working directory) by Infrastructure's connection-string builder — this
///     is a plain data holder only.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Path { get; init; } = "basil.db";
}
