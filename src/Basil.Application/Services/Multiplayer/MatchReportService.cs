using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Formats;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Builds the tournament match report (TRT) for a match from its persisted rows.
/// </summary>
/// <remarks>
///     The report is never persisted; it is assembled at read time from the <c>Matches</c>,
///     <c>Rounds</c>, and <c>Scores</c> tables, or from the live <see cref="MatchSession" /> for an
///     in-progress match. Round winners are derived from the stored scores per the round's win
///     condition.
/// </remarks>
public sealed class MatchReportService(
	IMatchRegistry matchRegistry,
	IMatchRepository matchRepository,
	IScoreRepository scores,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IUserRepository users,
	IBeatmapRepository beatmaps)
{
	/// <summary>Builds the full report for a match id.</summary>
	/// <param name="matchId">The database match id.</param>
	/// <param name="cancellationToken">A token that cancels the database lookups.</param>
	/// <returns>The <see cref="MatchReport" />, or <see langword="null" /> when no such match exists.</returns>
	public async Task<MatchReport?> BuildAsync(int matchId, CancellationToken cancellationToken = default)
	{
		var matchRow = await matchRepository.FetchMatchAsync(matchId, cancellationToken);
		if (matchRow is null) return null;

		// Call-local memoization only — this service is a singleton, so an instance-level cache
		// would leak stale data across concurrent report builds. The same handful of players and
		// often the same beatmap recur across every round and event of one report; resolving each
		// distinct id once here avoids repeating that resolution (including an uncached
		// FetchAllBySetIdAsync inside ResolveBeatmapAsync) once per round/event/score that
		// references it.
		var userCache = new Dictionary<int, UserBrief>();
		var beatmapCache = new Dictionary<string, BeatmapDetail?>();

		var roundRows = await matchRepository.FetchRoundsAsync(matchId, cancellationToken);
		var rounds = new List<MatchReportRound>(roundRows.Count);
		foreach (var round in roundRows)
		{
			var roundScores = await scores.FetchByRoundAsync(round.Id, cancellationToken);
			rounds.Add(await BuildRound(round, roundScores, userCache, beatmapCache, cancellationToken));
		}

		var events = await matchRepository.FetchEventsAsync(matchId, cancellationToken);
		var reportEvents = new List<MatchReportEvent>(events.Count);
		foreach (var e in events)
		{
			var actor = e.ActorUserId is { } actorId
				? await ResolveUserCached(actorId, userCache, cancellationToken) ?? Placeholder(actorId)
				: null;
			var target = e.TargetUserId is { } targetId
				? await ResolveUserCached(targetId, userCache, cancellationToken) ?? Placeholder(targetId)
				: null;
			reportEvents.Add(new MatchReportEvent((MatchEventType)e.EventType, actor, target,
				e.Timestamp.AsUtcOffset(), e.Detail));
		}

		var live = matchRegistry.GetByDbId(matchId);
		var liveInfo = live is null
			? null
			: await MatchLiveSnapshotBuilder.BuildRoomLive(live, beatmaps, cancellationToken);

		return new MatchReport(
			matchRow.Id, matchRow.Name, matchRow.CreatedAt.AsUtcOffset(), matchRow.EndedAt?.AsUtcOffset(),
			liveInfo, reportEvents, rounds);
	}

	/// <summary>Resolves a user id, caching only a successful resolution so each caller can still apply its own fallback.</summary>
	private async Task<UserBrief?> ResolveUserCached(int userId, Dictionary<int, UserBrief> cache,
		CancellationToken cancellationToken)
	{
		if (cache.TryGetValue(userId, out var cached)) return cached;
		var resolved =
			await UserBriefResolver.ResolveAsync(userId, gameRegistry, ircRegistry, users, cancellationToken);
		if (resolved is not null) cache[userId] = resolved;
		return resolved;
	}

	/// <summary>The same "unknown" placeholder <see cref="MatchLiveSnapshotBuilder.ResolveOrPlaceholder" /> uses.</summary>
	private static UserBrief Placeholder(int userId)
	{
		return new UserBrief(userId, "Unknown", Country.Xx);
	}

	/// <summary>Resolves a beatmap by md5, caching both a hit and a miss for the given md5.</summary>
	private async Task<BeatmapDetail?> ResolveBeatmapCached(string mapMd5, Dictionary<string, BeatmapDetail?> cache,
		CancellationToken cancellationToken)
	{
		if (cache.TryGetValue(mapMd5, out var cached)) return cached;
		var resolved = await MatchLiveSnapshotBuilder.ResolveBeatmapAsync(mapMd5, beatmaps, cancellationToken);
		cache[mapMd5] = resolved;
		return resolved;
	}

	/// <summary>Builds one round's report, deriving its winner and per-userSession scores from the stored rows.</summary>
	/// <param name="round">The round row.</param>
	/// <param name="roundScores">The round's stored scores.</param>
	/// <param name="userCache">The call-local user resolution cache shared across every round of this report.</param>
	/// <param name="beatmapCache">The call-local beatmap resolution cache shared across every round of this report.</param>
	/// <param name="cancellationToken">A token that cancels the user and beatmap lookups.</param>
	/// <returns>The <see cref="MatchReportRound" />.</returns>
	private async Task<MatchReportRound> BuildRound(Round round, IReadOnlyList<ScoreReport> roundScores,
		Dictionary<int, UserBrief> userCache, Dictionary<string, BeatmapDetail?> beatmapCache,
		CancellationToken cancellationToken)
	{
		int? winnerUserId = null;
		MatchTeam? winnerTeam = null;
		MatchWinCondition? winMetric = round.WinCondition;
		long? winDiff = null;

		switch (roundScores.Count)
		{
			case 0: // No players
				break;
			case 1:
			{
				// A single userSession wins by default, with a diff of 0
				var only = roundScores[0];
				if (roundScores.Any(s => s.Team is not null and not MatchTeam.Neutral))
					winnerTeam = only.Team ?? MatchTeam.Neutral;
				winnerUserId = only.UserId;
				winDiff = 0;
				break;
			}
			default:
			{
				if (roundScores.Any(s => s.Team is not null and not MatchTeam.Neutral))
				{
					// Team mode (≥2 players)
					var teams = roundScores
						.Where(s => s.Team is not null && s.Team != MatchTeam.Neutral)
						.GroupBy(s => s.Team)
						.ToList();

					if (teams.Count < 2)
					{
						// Only one team has players, so that team wins with a diff of 0
						winnerTeam = teams[0].Key ?? MatchTeam.Neutral;
						winDiff = 0;
					}
					else
					{
						var sorted = teams
							.Select(g => new
							{
								Team = g.Key,
								Total = g.Sum(s => GetMetric(s, round.WinCondition)),
								Players = g.ToList()
							})
							.OrderByDescending(t => t.Total)
							.ToList();

						if (sorted[0].Total == sorted[1].Total)
							// A draw, so no winner and a diff of 0
						{
							winDiff = 0;
						}
						else
						{
							winnerTeam = sorted[0].Team ?? MatchTeam.Neutral;
							winDiff = sorted[0].Total - sorted[1].Total;
						}
					}
				}
				else
				{
					// Individual mode (≥2 players)
					var sorted = roundScores
						.Select(s => new { s.UserId, Metric = GetMetric(s, round.WinCondition) })
						.OrderByDescending(s => s.Metric)
						.ToList();

					if (sorted[0].Metric == sorted[1].Metric)
						// A draw, so no winner and a diff of 0
					{
						winDiff = 0;
					}
					else
					{
						winnerUserId = sorted[0].UserId;
						winDiff = sorted[0].Metric - sorted[1].Metric;
					}
				}

				break;
			}
		}

		var winner = winnerUserId is { } wid
			? await ResolveUserCached(wid, userCache, cancellationToken) ?? Placeholder(wid)
			: null;

		var reportScores = new List<MatchReportScore>(roundScores.Count);
		foreach (var s in roundScores)
		{
			var user = await ResolveUserCached(s.UserId, userCache, cancellationToken)
			           ?? new UserBrief(s.UserId, s.UserName, Country.Xx);
			reportScores.Add(new MatchReportScore(
				user, s.Team, s.Mods, s.Score, s.Accuracy, s.MaxCombo,
				s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu,
				Enum.Parse<Grade>(s.Grade), s.Perfect, s.SubmittedAt.AsUtcOffset()));
		}

		var beatmap = await ResolveBeatmapCached(round.MapMd5, beatmapCache, cancellationToken);

		return new MatchReportRound(
			round.RoundIndex, round.MapMd5, beatmap,
			round.Mode, round.WinCondition, round.TeamType, round.Mods, round.Aborted,
			round.StartedAt.AsUtcOffset(), round.EndedAt?.AsUtcOffset(),
			winner, winnerTeam, winMetric, winDiff, reportScores);
	}

	/// <summary>Normalizes a score row to the metric a win condition compares.</summary>
	/// <remarks>
	///     Accuracy is scaled by 1000 to preserve 3 decimal places; combo compares max combo; everything else compares
	///     raw score.
	/// </remarks>
	/// <param name="s">The score row.</param>
	/// <param name="winCondition">The round's win condition.</param>
	/// <returns>The metric value to compare.</returns>
	private static long GetMetric(ScoreReport s, MatchWinCondition winCondition)
	{
		return winCondition switch
		{
			MatchWinCondition.Accuracy => (long)(s.Accuracy * 1000), // preserve 3 decimal places
			MatchWinCondition.Combo => s.MaxCombo,
			_ => s.Score
		};
	}
}

/// <summary>The tournament match report (TRT) for one match.</summary>
/// <param name="MatchId">The database match id.</param>
/// <param name="Name">The room name.</param>
/// <param name="CreatedAt">When the match was created.</param>
/// <param name="EndedAt">When the match ended, or <see langword="null" /> while it is still open.</param>
/// <param name="Live">
///     The live room configuration for an in-progress match, or <see langword="null" /> when not tracked in
///     memory.
/// </param>
/// <param name="Events">The match's lifecycle events.</param>
/// <param name="Rounds">The match's rounds.</param>
public sealed record MatchReport(
	int MatchId,
	string Name,
	DateTimeOffset CreatedAt,
	DateTimeOffset? EndedAt,
	MatchRoomLive? Live,
	IReadOnlyList<MatchReportEvent> Events,
	IReadOnlyList<MatchReportRound> Rounds);

/// <summary>One match lifecycle event.</summary>
/// <param name="EventType">The kind of event.</param>
/// <param name="Actor">The acting user, or <see langword="null" /> for system events.</param>
/// <param name="Target">The affected user, or <see langword="null" /> when the event has no target.</param>
/// <param name="Timestamp">When the event occurred.</param>
/// <param name="Detail">Optional event-specific detail text.</param>
public sealed record MatchReportEvent(
	MatchEventType EventType,
	UserBrief? Actor,
	UserBrief? Target,
	DateTimeOffset Timestamp,
	string? Detail);

/// <summary>One beatmap played within a match.</summary>
/// <remarks>
///     Only <c>MapMd5</c> is stored on the underlying round. <see cref="Beatmap" /> is resolved
///     live at report-build time via <see cref="IBeatmapRepository" />, and turns
///     <see langword="null" /> once that md5 no longer resolves (the content changed or was
///     removed).
/// </remarks>
/// <param name="RoundIndex">The 1-based round index within the match.</param>
/// <param name="MapMd5">The stored md5 of the beatmap played.</param>
/// <param name="Beatmap">The resolved beatmap detail, or <see langword="null" /> when the md5 no longer resolves.</param>
/// <param name="Mode">The game mode the round was played in.</param>
/// <param name="WinCondition">The win condition the round used.</param>
/// <param name="TeamType">The team type the round used.</param>
/// <param name="Mods">The mods the round used.</param>
/// <param name="Aborted">Whether the round was aborted.</param>
/// <param name="StartedAt">When the round started.</param>
/// <param name="EndedAt">When the round ended, or <see langword="null" /> while it is still open.</param>
/// <param name="Winner">The winning userSession, or <see langword="null" /> for a team win or a draw.</param>
/// <param name="WinnerTeam">The winning team, or <see langword="null" /> for an individual win or a draw.</param>
/// <param name="WinMetric">The win condition that determined the winner, when a winner exists.</param>
/// <param name="WinDiff">
///     The margin between the winner and the runner-up, or <see langword="null" /> when no winner was
///     determined.
/// </param>
/// <param name="Scores">The round's stored scores.</param>
public sealed record MatchReportRound(
	int RoundIndex,
	string MapMd5,
	BeatmapDetail? Beatmap,
	GameMode Mode,
	MatchWinCondition WinCondition,
	MatchTeamType TeamType,
	Mods Mods,
	bool Aborted,
	DateTimeOffset StartedAt,
	DateTimeOffset? EndedAt,
	UserBrief? Winner,
	MatchTeam? WinnerTeam,
	MatchWinCondition? WinMetric,
	long? WinDiff,
	IReadOnlyList<MatchReportScore> Scores);

/// <summary>One userSession's stored score within a round.</summary>
/// <param name="User">The userSession who submitted the score.</param>
/// <param name="Team">The team the userSession was on, or <see langword="null" /> for individual modes.</param>
/// <param name="Mods">The mods applied for the play.</param>
/// <param name="Score">The total score.</param>
/// <param name="Accuracy">The play's accuracy.</param>
/// <param name="MaxCombo">The maximum combo achieved.</param>
/// <param name="Num300">The number of 300 judgments.</param>
/// <param name="Num100">The number of 100 judgments.</param>
/// <param name="Num50">The number of 50 judgments.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="NumGeki">The number of geki judgments.</param>
/// <param name="NumKatu">The number of katu judgments.</param>
/// <param name="Grade">The awarded grade.</param>
/// <param name="Perfect">Whether the play was perfect.</param>
/// <param name="SubmittedAt">When the score was submitted.</param>
public sealed record MatchReportScore(
	UserBrief User,
	MatchTeam? Team,
	Mods Mods,
	long Score,
	double Accuracy,
	int MaxCombo,
	int Num300,
	int Num100,
	int Num50,
	int NumMiss,
	int NumGeki,
	int NumKatu,
	Grade Grade,
	bool Perfect,
	DateTimeOffset SubmittedAt);