using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps to change the given fields of a beatmapset on behalf of an actor.</summary>
/// <param name="Actor">The actor acting.</param>
/// <param name="Set">The stored beatmapset to change.</param>
/// <param name="Status">The new status, or <see langword="null" /> to keep it.</param>
/// <param name="Visible">The new visibility, or <see langword="null" /> to keep it.</param>
/// <param name="Locked">The new lock state, or <see langword="null" /> to keep it.</param>
/// <remarks>The fields are applied in the order status, visibility, lock; the answer is the first refusal, or ok.</remarks>
public sealed record PatchBeatmapset(
	IActor Actor,
	BeatmapsetRecord Set,
	BeatmapsetStatus? Status,
	bool? Visible,
	bool? Locked) : IRequest<Result<BeatmapsetOutcome>>;