using Basil.Domain.Scores;

namespace Basil.Domain.Multiplayer;

/// <summary>A match's results, round by round.</summary>
/// <param name="Match">The match these results describe.</param>
/// <param name="Rounds">The rounds and their results, in match order.</param>
public sealed record MatchReport(Match Match, IReadOnlyList<MatchReportRound> Rounds);

/// <summary>One round of a match report: its scores and its outcome.</summary>
/// <param name="Round">The round being reported.</param>
/// <param name="Scores">The scores submitted in the round.</param>
/// <param name="Result">The outcome decided from those scores, or <see langword="null" /> when there are none.</param>
public sealed record MatchReportRound(Round Round, IReadOnlyList<Score> Scores, RoundResult? Result);
