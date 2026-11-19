using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record SearchScores(UserReference? User, int? BeatmapId, BeatmapQuery? Beatmaps, PageRequest Page)
	: IRequest<ScoreListing>;