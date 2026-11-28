using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record GetBeatmapset(IActor? Actor, int SetId) : IRequest<BeatmapsetDetail?>;

/// <summary>Gets the sets with their difficulties, the actor may see; the answer keeps the order of <paramref name="SetIds"/> and leaves out a set that is missing or hidden.</summary>
public sealed record GetBeatmapsets(IActor? Actor, IReadOnlyList<int> SetIds) : IRequest<IReadOnlyList<BeatmapsetDetail>>;

public sealed record BeatmapsetDetail(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);