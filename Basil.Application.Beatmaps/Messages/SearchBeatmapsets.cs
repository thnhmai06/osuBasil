using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Actors;
using Basil.Application.Shared.Paging;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Application.Beatmaps.Messages;

public sealed record SearchBeatmapsets(IActor Actor, BeatmapQuery Query, PageRequest Page)
	: IRequest<Page<BeatmapsetRecord>>;