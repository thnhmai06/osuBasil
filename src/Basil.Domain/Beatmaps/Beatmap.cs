namespace Basil.Domain.Beatmaps;

/// <summary>
///     Ported from app/objects/beatmap.py's Beatmap fields. Star rating (Sr) is a direct
///     passthrough of osu!api's difficultyrating field — bancho.py does not run a local difficulty
///     calculator here.
/// </summary>
public sealed record Beatmap(

    #region Identity

    string Md5,
    int Id,
    Mapset Mapset,

    #endregion

    #region Metadata

    string Version,
    string Filename,

    #endregion

    #region Stats

    TimeSpan TotalLength,
    int MaxCombo,
    bool IsPrivate,
    int Plays,
    int Passes,
    Difficulty Difficulty

    #endregion

)
{
    //! Real osu! online ids are still well under this in 2026; a private-server-only
    //! local id range this far up the int32 space keeps collisions with real ids implausible
    //! without needing a dedicated id-space reservation table.
    public const int LocalIdFloor = 1_000_000_000;

    /// <summary>True for maps ingested without a real osu! online id (see <see cref="LocalIdFloor" />).</summary>
    public bool IsLocallyIngested => Id >= LocalIdFloor;

    /// <summary>Ported from Beatmap.full_name.</summary>
    public string FullName => $"{Mapset.Artist} - {Mapset.Title} [{Version}]";
}