using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps for a beatmapset with its beatmaps.</summary>
/// <param name="Actor">The actor that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="SetId">The id of the beatmapset.</param>
/// <remarks>
///     The answer is <see langword="null" /> when no beatmapset has the id or the actor may not see it; hidden beatmaps
///     are
///     left out of the list unless the actor may see them.
/// </remarks>
public sealed record GetBeatmapset(IActor? Actor, int SetId) : IRequest<BeatmapsetDetail?>;

/// <summary>The answer to <see cref="GetBeatmapset" />.</summary>
/// <param name="Set">The beatmapset.</param>
/// <param name="Beatmaps">The beatmaps the asker may see, ordered by id.</param>
public sealed record BeatmapsetDetail(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);