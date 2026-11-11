using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Asks Beatmaps for a page of the beatmapsets a query includes.</summary>
/// <param name="Actor">The actor that asks.</param>
/// <param name="Query">Which beatmapsets to include.</param>
/// <param name="Page">Which part of the listing to return.</param>
/// <remarks>Hidden beatmapsets are included only when the actor may see them.</remarks>
public sealed record SearchBeatmapsets(IActor Actor, BeatmapQuery Query, PageRequest Page)
	: IRequest<Page<BeatmapsetRecord>>;