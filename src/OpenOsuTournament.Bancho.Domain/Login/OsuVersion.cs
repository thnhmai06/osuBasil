namespace OpenOsuTournament.Bancho.Domain.Login;

/// <summary>Ported from app/objects/player.py's OsuStream (StrEnum).</summary>
public enum OsuStream
{
    Stable,
    Beta,
    CuttingEdge,
    Tourney,
    Dev
}

/// <summary>Ported from app/objects/player.py's OsuVersion. e.g. b20200201.2cuttingedge.</summary>
public sealed record OsuVersion(DateOnly Date, int? Revision, OsuStream Stream);