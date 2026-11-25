using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

/// <summary>Searches beatmapsets for a supporter and returns the page with the details of its sets.</summary>
public sealed record SearchBeatmapsetsDirect(IActor Actor, BeatmapQuery Query, PageRequest Page)
	: IRequest<(BeatmapDirectResult Result, Page<BeatmapsetRecord> Page, IReadOnlyList<BeatmapsetDetail> Details)>;

/// <summary>Gets one beatmapset with its difficulties for a supporter.</summary>
public sealed record GetBeatmapsetDirect(IActor Actor, int SetId)
	: IRequest<(BeatmapDirectResult Result, BeatmapsetDetail? Detail)>;

/// <summary>Opens the archive of a beatmapset for a supporter.</summary>
public sealed record OpenBeatmapsetArchiveDirect(IActor Actor, int SetId, bool WithVideo)
	: IRequest<(BeatmapDirectResult Result, Stream? Archive)>;