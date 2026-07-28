using System.Text.Json.Serialization;
using Basil.Application.Json;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Shared shape for every beatmap embed/response — never carries <see cref="Beatmap.Filename" />
///     (internal, `[JsonIgnore]`d on the domain record) or a parent beatmapset reference (that's
///     <see cref="BeatmapInSet" /> vs. <see cref="BeatmapDetail" />'s split, to avoid the
///     beatmap-in-set-in-beatmap cycle). <see cref="TotalLength" /> stays <see cref="TimeSpan" /> on
///     the C# type (matching the domain record) but wires out as whole seconds via
///     <see cref="TimeSpanSecondsJsonConverter" />.
/// </summary>
public abstract record BeatmapView(
    string Md5,
    int Id,
    string Version,
    [property: JsonConverter(typeof(TimeSpanSecondsJsonConverter))]
    TimeSpan TotalLength,
    int MaxCombo,
    Difficulty Difficulty,
    IReadOnlyDictionary<string, int> ObjectCounts,
    bool IsLocallyIngested);

/// <summary>
///     Used inside <see cref="BeatmapsetDetail" />'s <c>Beatmaps</c> list — no parent beatmapset embed (avoids the
///     cycle).
/// </summary>
public sealed record BeatmapInSet(
    string Md5,
    int Id,
    string Version,
    TimeSpan TotalLength,
    int MaxCombo,
    Difficulty Difficulty,
    IReadOnlyDictionary<string, int> ObjectCounts,
    bool IsLocallyIngested)
    : BeatmapView(Md5, Id, Version, TotalLength, MaxCombo, Difficulty, ObjectCounts, IsLocallyIngested);

/// <summary>
///     Used everywhere else a beatmap is embedded (score, match round, live snapshot, GET
///     /beatmapsets/{setId}/{beatmapId}) — carries the parent beatmapset.
/// </summary>
public sealed record BeatmapDetail(
    string Md5,
    int Id,
    string Version,
    TimeSpan TotalLength,
    int MaxCombo,
    Difficulty Difficulty,
    IReadOnlyDictionary<string, int> ObjectCounts,
    bool IsLocallyIngested,
    BeatmapsetSummary Beatmapset)
    : BeatmapView(Md5, Id, Version, TotalLength, MaxCombo, Difficulty, ObjectCounts, IsLocallyIngested);

/// <summary>
///     API-facing name for a beatmapset (the domain/internal type stays <see cref="Mapset" /> — not
///     renamed). Used both as a list item for `GET /beatmapsets` and as the parent embed on
///     <see cref="BeatmapDetail" />.
/// </summary>
public sealed record BeatmapsetSummary(
    int Id,
    string Artist,
    string Title,
    string Creator,
    DateTime LastUpdate,
    DateTime CreatedAt,
    bool IsFrozen,
    bool IsPrivate,
    RankedStatus Status,
    int BeatmapCount);

/// <summary>
///     Payload for `GET /beatmapsets/{mapsetId}` — every difficulty under the set, each as a
///     <see cref="BeatmapInSet" />.
/// </summary>
public sealed record BeatmapsetDetail(
    int Id,
    string Artist,
    string Title,
    string Creator,
    DateTime LastUpdate,
    DateTime CreatedAt,
    bool IsFrozen,
    bool IsPrivate,
    RankedStatus Status,
    IReadOnlyList<BeatmapInSet> Beatmaps);

/// <summary>
///     Maps the domain <see cref="Mapset" />/<see cref="Beatmap" /> records onto the API-facing view
///     types above.
/// </summary>
public static class BeatmapViewMapper
{
    public static BeatmapsetSummary ToSummary(this Mapset mapset, int beatmapCount)
    {
        return new BeatmapsetSummary(mapset.Id, mapset.Artist, mapset.Title, mapset.Creator, mapset.LastUpdate,
            mapset.CreatedAt, mapset.IsFrozen, mapset.IsPrivate, mapset.Status, beatmapCount);
    }

    public static BeatmapsetDetail ToDetail(this Mapset mapset, IReadOnlyList<Beatmap> beatmaps)
    {
        return new BeatmapsetDetail(mapset.Id, mapset.Artist, mapset.Title, mapset.Creator, mapset.LastUpdate,
            mapset.CreatedAt, mapset.IsFrozen, mapset.IsPrivate, mapset.Status,
            beatmaps.Select(b => b.ToInSet()).ToList());
    }

    public static BeatmapInSet ToInSet(this Beatmap beatmap)
    {
        return new BeatmapInSet(beatmap.Md5, beatmap.Id, beatmap.Version, beatmap.TotalLength, beatmap.MaxCombo,
            beatmap.Difficulty, beatmap.ObjectCounts, beatmap.IsLocallyIngested);
    }

    public static BeatmapDetail ToDetail(this Beatmap beatmap, BeatmapsetSummary beatmapset)
    {
        return new BeatmapDetail(beatmap.Md5, beatmap.Id, beatmap.Version, beatmap.TotalLength, beatmap.MaxCombo,
            beatmap.Difficulty, beatmap.ObjectCounts, beatmap.IsLocallyIngested, beatmapset);
    }
}