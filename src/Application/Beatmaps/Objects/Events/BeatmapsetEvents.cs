using Basil.Application.Shared.Events;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Objects.Events;

public abstract record BeatmapsetEvent : Event;

public sealed record BeatmapsetImported(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps) : BeatmapsetEvent;

public sealed record BeatmapsetDeleted(BeatmapsetRecord Set) : BeatmapsetEvent;