namespace Bancho.Application.Configuration;

/// <summary>
/// Ports DB_HOST/DB_PORT/DB_USER/DB_PASS/DB_NAME from app/settings.py. The connection-string
/// building (with proper escaping for the chosen driver) belongs in Infrastructure, not here —
/// this is a plain data holder only.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public required string Host { get; init; }
    public int Port { get; init; } = 3306;
    public required string User { get; init; }
    public required string Password { get; init; }
    public required string Name { get; init; }
}
