using Basil.Application.Common.Events;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps;

/// <summary>Something happened to the stored beatmaps.</summary>
public abstract record BeatmapEvent : Event;

/// <summary>A beatmapset was imported or re-imported.</summary>
/// <param name="Set">The imported beatmapset.</param>
/// <param name="Beatmaps">Its difficulties after the import.</param>
public sealed record BeatmapsetImported(Beatmapset Set, IReadOnlyList<Beatmap> Beatmaps) : BeatmapEvent;