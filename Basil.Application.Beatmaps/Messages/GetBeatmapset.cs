using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record GetBeatmapset(IActor? Actor, int SetId) : IRequest<BeatmapsetDetail?>;

public sealed record BeatmapsetDetail(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);