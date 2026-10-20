using System.Collections.Immutable;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Storage.Postgres;
using Basil.Infrastructure.Storage.Memory;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores which round each score belongs to.</summary>
internal sealed class PostgresRoundScoreRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IRoundRepository rounds,
	IScoreRepository scores) : IRoundScoreRepository
{
	private readonly OwnedLists<(int MatchId, int Index), Round, RoundScore> _byRound = new();

	/// <inheritdoc />
	public async Task<RoundScore> RecordAsync(RoundScore roundScore, CancellationToken cancellationToken = default)
	{
		var round = await LiveAsync(roundScore.Round, cancellationToken).ConfigureAwait(false);
		var saved = writer.SaveAsync(Root.Match(round.Match.Id), (GetType(), roundScore.Score.Id), new WriteCommand(
			"""
			insert into round_scores (score_id, match_id, round_number, team)
			values (@ScoreId, @MatchId, @RoundNumber, @Team)
			on conflict (score_id) do update set match_id = excluded.match_id, round_number = excluded.round_number, team = excluded.team
			""",
			new
			{
				ScoreId = roundScore.Score.Id,
				MatchId = round.Match.Id,
				RoundNumber = round.Index,
				Team = roundScore.Team is { } team ? (int)team : (int?)null
			}));
		_byRound.Change((round.Match.Id, round.Index), round, list => Insert(list, roundScore), saved);
		return roundScore;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken cancellationToken = default)
	{
		var live = await LiveAsync(round, cancellationToken).ConfigureAwait(false);
		return await _byRound.GetOrLoadAsync((live.Match.Id, live.Index), live, async () =>
		{
			var rows = await reader.ReadAsync(connection => connection.QueryAsync<RoundScoreRow>(
				"""
				select score_id, team
				from round_scores
				where match_id = @MatchId and round_number = @Number
				order by score_id
				""",
				new { MatchId = live.Match.Id, Number = live.Index }), cancellationToken).ConfigureAwait(false);

			var result = ImmutableList.CreateBuilder<RoundScore>();
			foreach (var row in rows)
				if (await scores.GetAsync(row.ScoreId, cancellationToken).ConfigureAwait(false) is { } score)
					result.Add(new RoundScore
					{
						Score = score,
						Round = live,
						Team = row.Team is { } team ? (GameTeam)team : null
					});
			return result.ToImmutable();
		}).ConfigureAwait(false);
	}

	/// <summary>Finds the live instance of a round, which owns its list of scores.</summary>
	private async Task<Round> LiveAsync(Round round, CancellationToken cancellationToken)
	{
		var matchRounds = await rounds.ListAsync(round.Match, cancellationToken).ConfigureAwait(false);
		return matchRounds.FirstOrDefault(item => item.Equals(round)) ?? round;
	}

	/// <summary>Adds a round score, replacing the one of the same score, keeping the list in submission order.</summary>
	private static ImmutableList<RoundScore> Insert(ImmutableList<RoundScore> list, RoundScore roundScore)
	{
		var rest = list.RemoveAll(item => item.Score.Equals(roundScore.Score));
		var index = rest.FindIndex(item => item.Score.Id > roundScore.Score.Id);
		return index < 0 ? rest.Add(roundScore) : rest.Insert(index, roundScore);
	}

	/// <summary>A stored row of the <c>round_scores</c> table.</summary>
	private sealed class RoundScoreRow
	{
		public int ScoreId { get; set; }
		public int? Team { get; set; }
	}
}