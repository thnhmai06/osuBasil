using Basil.Application.Shared.Paging;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record ListScores(int? BeatmapId, int? UserId, PageRequest Page) : IRequest<ScoreListing>;

public sealed record ScoreListing(ScoreListResult Outcome, Page<ScoreRecord>? Scores);

public enum ScoreListResult
{
	Denied = 0,

	Ok = 1,

	NoBeatmap,

	NoUser
}