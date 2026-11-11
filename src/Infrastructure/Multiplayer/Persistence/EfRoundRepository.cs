using System.Text.Json;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Shared;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class EfRoundRepository(DatabaseSession database, IRoundScoreRepository roundScores)
	: IRoundRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round)
	{
		var row = ToRow(round);
		return database.WriteAsync((MultiplayerDbContext context) =>
				context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number));
	}

	/// <inheritdoc />
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
				round.Result = RoundResult.Restore(round, await roundScores.ListAsync(round, ct), stored.ScoreOrder,
					stored.Margin, stored.IsDraw, stored.WinnerId, stored.Winner, stored.RedScore, stored.BlueScore);
			listed.Add(round);
		}

		return listed;
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

	/// <summary>Builds a round of the given match from its stored row.</summary>
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
			BeatmapHash = new Md5(row.BeatmapHash),
			Settings = settings,
			StartedAt = row.StartedAt,
			EndedAt = row.EndedAt,
			Aborted = row.Aborted
		};
	}

	/// <summary>The decision of a round as the <c>result</c> column keeps it; the property names are the stored JSON names.</summary>
	private sealed record StoredDecision(
		IReadOnlyList<int> ScoreOrder,
		int? WinnerId,
		GameTeam? Winner,
		double Margin,
		bool IsDraw,
		double? RedScore,
		double? BlueScore)
	{
		public static StoredDecision Of(RoundResult result)
		{
			var order = (IReadOnlyList<int>) [.. result.Scores.Select(score => score.Score.Id)];
			return result switch
			{
				TeamVersusResult team => new StoredDecision(order, null, team.Winner, team.Margin, team.IsDraw,
					team.TeamScore[GameTeam.Red], team.TeamScore[GameTeam.Blue]),
				HeadToHeadResult head => new StoredDecision(order, head.WinnerId, null, head.Margin, head.IsDraw, null,
					null),
				_ => throw new ArgumentOutOfRangeException(nameof(result))
			};
		}
	}
}