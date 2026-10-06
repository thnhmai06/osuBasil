using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Utilities;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class SqliteRoundRepository(Database database) : IRoundRepository
{
	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Rounds (MatchId, Number, BeatmapHash, Mode, Mods, Freemods, TeamType, WinCondition, Seed, StartedAt, EndedAt, Aborted)
			VALUES (@MatchId, @Number, @BeatmapHash, @Mode, @Mods, @Freemods, @TeamType, @WinCondition, @Seed, @StartedAt, @EndedAt, @Aborted)
			ON CONFLICT(MatchId, Number) DO UPDATE SET
				BeatmapHash = excluded.BeatmapHash,
				Mode = excluded.Mode,
				Mods = excluded.Mods,
				Freemods = excluded.Freemods,
				TeamType = excluded.TeamType,
				WinCondition = excluded.WinCondition,
				Seed = excluded.Seed,
				StartedAt = excluded.StartedAt,
				EndedAt = excluded.EndedAt,
				Aborted = excluded.Aborted
			""",
			new
			{
				MatchId = round.Match.Id,
				round.Number,
				BeatmapHash = round.BeatmapHash.HashValue,
				Mode = (long)round.Settings.Mode,
				Mods = (long)round.Settings.Mods,
				Freemods = round.Settings.Freemods ? 1 : 0,
				TeamType = (long)round.Settings.TeamType,
				WinCondition = (long)round.Settings.WinCondition,
				round.Settings.Seed,
				StartedAt = round.StartedAt.ToUnixTimeMilliseconds(),
				EndedAt = round.EndedAt?.ToUnixTimeMilliseconds(),
				Aborted = round.Aborted ? 1 : 0
			});
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<RoundRow>(
			"""
			SELECT Number, BeatmapHash, Mode, Mods, Freemods, TeamType, WinCondition, Seed, StartedAt, EndedAt, Aborted
			FROM Rounds
			WHERE MatchId = @MatchId
			ORDER BY Number
			""",
			new { MatchId = match.Id });

		return rows.Select(row => ToRound(match, row)).ToList();
	}

	/// <summary>Builds a round of the given match from its stored row.</summary>
	private static Round ToRound(Match match, RoundRow row)
	{
		// The mods setter validates against the mode, so the stored mode must be restored first;
		// the seed is init-only and belongs to the object initializer.
		var settings = new MatchSettings
		{
			Mode = (GameMode)row.Mode,
			Mods = (GameMods)row.Mods,
			Freemods = row.Freemods != 0,
			TeamType = (GameTeamType)row.TeamType,
			WinCondition = (GameWinCondition)row.WinCondition,
			Seed = row.Seed
		};

		return new Round
		{
			Match = match,
			Number = row.Number,
			BeatmapHash = new Md5(row.BeatmapHash),
			Settings = settings,
			StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAt),
			EndedAt = row.EndedAt is { } endedAt ? DateTimeOffset.FromUnixTimeMilliseconds(endedAt) : null,
			Aborted = row.Aborted != 0
		};
	}

	/// <summary>A stored row of the Rounds table.</summary>
	private sealed class RoundRow
	{
		public int Number { get; set; }
		public string BeatmapHash { get; set; } = "";
		public long Mode { get; set; }
		public long Mods { get; set; }
		public long Freemods { get; set; }
		public long TeamType { get; set; }
		public long WinCondition { get; set; }
		public int Seed { get; set; }
		public long StartedAt { get; set; }
		public long? EndedAt { get; set; }
		public long Aborted { get; set; }
	}
}
