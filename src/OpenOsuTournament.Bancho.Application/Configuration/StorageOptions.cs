namespace OpenOsuTournament.Bancho.Application.Configuration;

/// <summary>Ports REPLAYS_PATH from app/api/dependencies.py (".data/osr" relative to the process's working directory).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string ReplaysPath { get; init; } = Path.Combine(".data", "osr");
}