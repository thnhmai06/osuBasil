using System.Text.Json;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Shared.Structures.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

internal sealed class EfRoundRepository(DatabaseSession database, IRoundScoreRepository roundScores)
	: IRoundRepository
{
	public Task CreateOrUpdateAsync(Round round)
	{
		var row = ToRow(round);
		return database.WriteAsync((MultiplayerDbContext context) =>
			context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number));
	}

	public async Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.Rounds
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Number)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		var listed = new List<Round>(rows.Count);
		foreach (var row in rows)
		{
			var round = ToRound(match, row);
			// A stored decision is shown as it was decided, never decided again from the scores.
			if (row.Result is not null && JsonSerializer.Deserialize<StoredDecision>(row.Result) is { } stored)
			{
				var scores = await roundScores.ListAsync(round, ct);
				round.Result = round.Settings.TeamType.IsTeamMode()
					? new TeamVersusResult(round, stored.Margin, stored.IsDraw, stored.Winner,
						new Dictionary<GameTeam, RoundTeamScore>
						{
							[GameTeam.Red] = TeamScoreOf(round, scores, GameTeam.Red, stored.RedPassedScore,
								stored.RedScore),
							[GameTeam.Blue] = TeamScoreOf(round, scores, GameTeam.Blue, stored.BluePassedScore,
								stored.BlueScore)
						})
					: new HeadToHeadResult(round, scores, stored.Margin, stored.IsDraw, stored.WinnerId);
			}

			listed.Add(round);
		}

		return listed;
	}

	private static RoundTeamScore TeamScoreOf(Round round, IEnumerable<RoundScore> scores, GameTeam team,
		double? passedScore, double? totalScore)
	{
		return new RoundTeamScore(round.Settings.WinCondition,
			scores.Where(score => score.Team == team).Select(score => score.Score).ToArray(),
			passedScore ?? 0, totalScore ?? 0);
	}

	private static RoundRow ToRow(Round round)
	{
		return new RoundRow
		{
			MatchId = round.Match.Id,
			Number = round.Index,
			BeatmapHash = round.BeatmapHash.HashValue,
			Mode = (int)round.Settings.Mode,
			Mods = (int)round.Settings.Mods,
			Freemods = round.Settings.Freemods,
			TeamType = (int)round.Settings.TeamType,
			WinCondition = (int)round.Settings.WinCondition,
			Seed = round.Settings.Seed,
			StartedAt = round.StartedAt.ToUniversalTime(),
			EndedAt = round.EndedAt?.ToUniversalTime(),
			Aborted = round.Aborted,
			Result = round.Result is { } result ? JsonSerializer.Serialize(StoredDecision.Of(result)) : null
		};
	}

	private static Round ToRound(MatchRecord match, RoundRow row)
	{
		// The mods setter validates against the mode, so the stored mode must be restored first;
		// the seed is init-only and belongs to the object initializer.
		var settings = new MatchSettings
		{
			Mode = (GameMode)row.Mode,
			Mods = (GameMods)row.Mods,
			Freemods = row.Freemods,
			TeamType = (GameTeamType)row.TeamType,
			WinCondition = (GameWinCondition)row.WinCondition,
			Seed = row.Seed
		};

		return new Round
		{
			Match = match,
			Index = row.Number,
			BeatmapHash = new Md5Hash(row.BeatmapHash),
			Settings = settings,
			StartedAt = row.StartedAt,
			EndedAt = row.EndedAt,
			Aborted = row.Aborted
		};
	}

	private sealed record StoredDecision(
		int? WinnerId,
		GameTeam? Winner,
		double Margin,
		bool IsDraw,
		double? RedPassedScore,
		double? BluePassedScore,
		double? RedScore,
		double? BlueScore)
	{
		public static StoredDecision Of(RoundResult result)
		{
			return result switch
			{
				TeamVersusResult team => new StoredDecision(null, team.Winner, team.WinMargin, team.IsDraw,
					team.TeamScores[GameTeam.Red].PassedScore, team.TeamScores[GameTeam.Blue].PassedScore,
					team.TeamScores[GameTeam.Red].TotalScore, team.TeamScores[GameTeam.Blue].TotalScore),
				HeadToHeadResult head => new StoredDecision(head.WinnerId, null, head.WinMargin, head.IsDraw, null,
					null, null, null),
				_ => throw new ArgumentOutOfRangeException(nameof(result))
			};
		}
	}
}