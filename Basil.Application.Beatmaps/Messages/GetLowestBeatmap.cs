using Basil.Application.Shared.Actors;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Gets the lowest difficulty of a set, the one with the lowest id, among those the actor may see.</summary>
/// <remarks>The answer says whether the actor sees the set at all, and gives <see langword="null"/> as the beatmap when the set has no visible difficulty.</remarks>
public sealed record GetLowestBeatmap(IActor? Actor, int SetId) : IRequest<(bool SetFound, BeatmapRecord? Beatmap)>;
