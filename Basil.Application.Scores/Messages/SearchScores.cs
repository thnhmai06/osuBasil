using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record SearchScores(int? UserId, string? UserName, int? BeatmapId, BeatmapQuery? Beatmaps, PageRequest Page)
	: IRequest<ScoreListing>;