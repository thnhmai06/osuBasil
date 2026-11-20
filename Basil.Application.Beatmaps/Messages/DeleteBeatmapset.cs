using Basil.Application.Beatmaps.Objects;
using Basil.Application.Shared.Actors;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record DeleteBeatmapset(IActor Actor, int SetId)
	: IRequest<BeatmapsetEditResult>;