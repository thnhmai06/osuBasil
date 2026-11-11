using Basil.Application.Beatmaps.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps to delete a beatmapset on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
/// <param name="SetId">The id of the beatmapset to delete.</param>
public sealed record DeleteBeatmapset(IActor Actor, int SetId)
	: IRequest<BeatmapsetOutcome>;