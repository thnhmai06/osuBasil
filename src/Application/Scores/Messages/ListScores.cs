using Basil.Application.Shared;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Application.Scores.Messages;

/// <summary>Asks Scores for a page of scores, newest first.</summary>
/// <param name="BeatmapId">Only the scores on this beatmap, or <see langword="null" /> for any.</param>
/// <param name="UserId">Only the scores of this user, or <see langword="null" /> for any.</param>
/// <param name="Page">Which part of the listing to return.</param>
/// <remarks>Scores are public.</remarks>
public sealed record ListScores(int? BeatmapId, int? UserId, PageRequest Page) : IRequest<ScoreListing>;

/// <summary>The answer to <see cref="ListScores" />.</summary>
/// <param name="Outcome">Ok, or which filter named something that does not exist.</param>
/// <param name="Scores">The page of scores, or <see langword="null" /> when the outcome is not ok.</param>
public sealed record ScoreListing(ScoreListOutcome Outcome, Page<ScoreRecord>? Scores);

/// <summary>What <see cref="ListScores" /> answers.</summary>
public enum ScoreListOutcome
{
	/// <summary>The scores were listed.</summary>
	Ok = 1,

	/// <summary>No beatmap has the requested id.</summary>
	NoBeatmap,

	/// <summary>No user has the requested id.</summary>
	NoUser
}