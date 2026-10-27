using Basil.Application.Common.Events;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Runtime;

/// <summary>Something happened to the stored beatmaps.</summary>
public abstract record BeatmapsetEvent : Event;

/// <summary>A beatmapset was imported or re-imported.</summary>
/// <param name="Set">The imported beatmapset.</param>
/// <param name="Beatmaps">Its difficulties after the import.</param>
public sealed record BeatmapsetImported(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps) : BeatmapsetEvent;

/// <summary>A beatmapset was deleted with its beatmaps and archive.</summary>
/// <param name="Set">The deleted beatmapset.</param>
public sealed record BeatmapsetDeleted(BeatmapsetRecord Set) : BeatmapsetEvent;