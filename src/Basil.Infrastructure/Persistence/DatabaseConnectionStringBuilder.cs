using Basil.Application.Configuration;

namespace Basil.Infrastructure.Persistence;

/// <summary>Shared by DI registration and the startup migration runner so both build the identical connection string.</summary>
public static class DatabaseConnectionStringBuilder
{
    /// <summary>Resolves <see cref="DatabaseOptions.Path" /> to an absolute path, anchored to the
    ///     executable's directory (not the process's working directory) when relative.</summary>
    public static string ResolvePath(DatabaseOptions options)
    {
        return Path.IsPathRooted(options.Path) ? options.Path : Path.Combine(AppContext.BaseDirectory, options.Path);
    }

    public static string Build(DatabaseOptions options)
    {
        // Foreign Keys=True: SQLite disables FK enforcement per-connection by default; the schema
        // declares FKs (e.g. UserStats -> Users) and MySQL always enforced them.
        // Default Timeout: maps to SQLite's busy_timeout. The server is deliberately multithreaded
        // (see MatchSession.Lock) and writes across different matches are not serialized, so
        // concurrent writers are expected — without this they'd throw SQLITE_BUSY immediately
        // instead of waiting.
        return $"Data Source={ResolvePath(options)};Foreign Keys=True;Default Timeout=5";
    }
}
