using System.Text.Json.Serialization;

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
    int Plays,
    int Passes,
    Difficulty Difficulty,
    IReadOnlyDictionary<string, int> ObjectCounts,

    #endregion

    #region Background

    [property: JsonIgnore] string? BackgroundFile = null

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

    /// <summary>
    ///     Hand-written to replace the compiler-generated record equality: <see cref="ObjectCounts" />
    ///     is a plain <see cref="IReadOnlyDictionary{TKey,TValue}" />, which has no structural
    ///     <c>Equals</c>/<c>GetHashCode</c> of its own (two dictionaries with identical entries but
    ///     different instances would otherwise compare unequal, exactly the case after a DB
    ///     round-trip deserializes a fresh dictionary).
    /// </summary>
    public bool Equals(Beatmap? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Md5 == other.Md5 && Id == other.Id && Mapset == other.Mapset &&
            Version == other.Version && Filename == other.Filename &&
            TotalLength == other.TotalLength && MaxCombo == other.MaxCombo &&
            Plays == other.Plays && Passes == other.Passes && Difficulty == other.Difficulty &&
            BackgroundFile == other.BackgroundFile &&
            ObjectCounts.Count == other.ObjectCounts.Count &&
            ObjectCounts.OrderBy(kv => kv.Key).SequenceEqual(other.ObjectCounts.OrderBy(kv => kv.Key));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Md5);
        hash.Add(Id);
        hash.Add(Mapset);
        hash.Add(Version);
        hash.Add(Filename);
        hash.Add(TotalLength);
        hash.Add(MaxCombo);
        hash.Add(Plays);
        hash.Add(Passes);
        hash.Add(Difficulty);
        hash.Add(BackgroundFile);
        foreach (var kv in ObjectCounts.OrderBy(kv => kv.Key))
        {
            hash.Add(kv.Key);
            hash.Add(kv.Value);
        }

        return hash.ToHashCode();
    }
}