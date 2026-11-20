using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record GetBeatmap(IActor? Actor, int BeatmapId) : IRequest<BeatmapRecord?>;

public sealed record FindBeatmap(int BeatmapId) : IRequest<BeatmapRecord?>;