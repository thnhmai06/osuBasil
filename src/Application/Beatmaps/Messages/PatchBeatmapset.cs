using Basil.Application.Beatmaps.Objects;
using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record PatchBeatmapset(
	IActor Actor,
	int SetId,
	BeatmapsetStatus? Status,
	bool? Visible,
	bool? Locked) : IRequest<BeatmapsetOutcome>;