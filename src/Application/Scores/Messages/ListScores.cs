using Basil.Application.Shared.Paging;
using Basil.Domain.Game.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

public sealed record ListScores(int? BeatmapId, int? UserId, PageRequest Page) : IRequest<ScoreListing>;

public sealed record ScoreListing(ScoreListOutcome Outcome, Page<ScoreRecord>? Scores);

public enum ScoreListOutcome
{
	Ok = 1,

	NoBeatmap,

	NoUser
}