using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps to delete a beatmapset on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
/// <param name="Set">The stored beatmapset to delete.</param>
public sealed record DeleteBeatmapset(IActor Actor, BeatmapsetRecord Set)
	: IRequest<Result<BeatmapsetOutcome>>;