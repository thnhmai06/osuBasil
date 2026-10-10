using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Match;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Round;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class PostgresRoundScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IScoreRepository scores) : IRoundScoreRepository
{
	/// <inheritdoc />
	public async Task<RoundScore> RecordAsync(RoundScore roundScore, CancellationToken cancellationToken = default)
	{
		await writer.WriteAsync(Root.Match(roundScore.Round.Match.Id), (connection, transaction) =>
			connection.ExecuteAsync(
				"""
				insert into round_scores (score_id, match_id, round_number, team)
				values (@ScoreId, @MatchId, @RoundNumber, @Team)
				on conflict (score_id) do update set team = excluded.team
				""",
				new
				{
					ScoreId = roundScore.Score.Id,
					MatchId = roundScore.Round.Match.Id,
					RoundNumber = roundScore.Round.Number,
					Team = roundScore.Team is { } team ? (int)team : (int?)null
				}, transaction), cancellationToken);
		return roundScore;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default)
	{
		var rows = await reader.ReadAsync(connection => connection.QueryAsync<RoundScoreRow>(
			"""
			select score_id, team
			from round_scores
			where match_id = @MatchId and round_number = @Number
			order by score_id
			""",
			new { MatchId = round.Match.Id, round.Number }), cancellationToken);

		var result = ImmutableList.CreateBuilder<RoundScore>();
		foreach (var row in rows)
		{
			var score = await scores.GetAsync(row.ScoreId, cancellationToken);
			if (score is null)
				continue;

			result.Add(new RoundScore
			{
				Score = score,
				Round = round,
				Team = row.Team is { } team ? (GameTeam)team : null
			});
		}

		return result.ToImmutable();
	}

	/// <summary>A stored row of the <c>round_scores</c> table.</summary>
	private sealed class RoundScoreRow
	{
		public int ScoreId { get; set; }
		public int? Team { get; set; }
	}
}