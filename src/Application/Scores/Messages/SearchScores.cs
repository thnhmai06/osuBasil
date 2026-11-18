using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Domain.Game.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record SearchScores(UserReference? User, int? BeatmapId, BeatmapQuery? Beatmaps, PageRequest Page)
	: IRequest<ScoreListing>;
