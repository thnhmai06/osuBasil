namespace OpenOsuTournament.Bancho.Application.Configuration;

/// <summary>Ports APP_HOST / APP_PORT from app/settings.py.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public required string Host { get; init; }
    public int Port { get; init; }
}