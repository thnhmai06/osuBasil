using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps for one beatmap with its beatmapset.</summary>
/// <param name="Actor">The actor that asks, or <see langword="null" /> for a caller without a session.</param>
/// <param name="BeatmapId">The id of the beatmap.</param>
/// <remarks>The answer is <see langword="null" /> when no beatmap has the id or the actor may not see it.</remarks>
public sealed record GetBeatmap(IActor? Actor, int BeatmapId) : IRequest<BeatmapRecord?>;

/// <summary>Asks Beatmaps for one beatmap whatever its visibility, for work the server does for itself.</summary>
/// <param name="BeatmapId">The id of the beatmap.</param>
/// <remarks>The answer is <see langword="null" /> when no beatmap has the id.</remarks>
public sealed record FindBeatmap(int BeatmapId) : IRequest<BeatmapRecord?>;

/// <summary>Asks Beatmaps for the star rating of a beatmap under some mods, for work the server does for itself.</summary>
/// <param name="Beatmap">The beatmap.</param>
/// <param name="Mods">The mods of the play.</param>
/// <remarks>
///     The rating is for the game mode the beatmap is made for. The answer is <see langword="null" /> when the rating cannot
///     be calculated, for example when the beatmap's file is missing.
/// </remarks>
public sealed record GetBeatmapStars(BeatmapRecord Beatmap, GameMods Mods) : IRequest<double?>;
