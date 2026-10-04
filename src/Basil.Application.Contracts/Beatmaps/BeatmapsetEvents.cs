using Basil.Application.Contracts.Events;

namespace Basil.Application.Contracts.Beatmaps;

/// <summary>Something happened to the stored beatmaps.</summary>
public abstract record BeatmapsetEvent : Event;

/// <summary>A beatmapset was imported or re-imported.</summary>
/// <param name="Set">The imported beatmapset.</param>
/// <param name="Beatmaps">Its difficulties after the import.</param>
public sealed record BeatmapsetImported(Beatmapset Set, IReadOnlyList<Beatmap> Beatmaps) : BeatmapsetEvent;

/// <summary>A beatmapset was deleted with its beatmaps and archive.</summary>
/// <param name="Set">The deleted beatmapset.</param>
public sealed record BeatmapsetDeleted(Beatmapset Set) : BeatmapsetEvent;