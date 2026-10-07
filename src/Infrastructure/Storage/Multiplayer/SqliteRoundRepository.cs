using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class SqliteRoundRepository(
	Database database,
	WriteBuffer buffer,
	IMatchRepository matches,
	MatchReportCache reports)
	: CachedRepository<(int MatchId, int Number), Round>(database, buffer), IRoundRepository
{
	private readonly IdentityMap<int, ImmutableList<Round>> _byMatch = new();

	protected override (int MatchId, int Number) KeyOf(Round item) => (item.Match.Id, item.Number);

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		var live = Track(round);
		if (!ReferenceEquals(live, round))
		{
			live.EndedAt = round.EndedAt;
			live.Aborted = round.Aborted;
		}

		_byMatch.TryUpdate(live.Match.Id, current => AddOrReplace(current, live));
		Save(live);
		reports.Invalidate(live.Match.Id);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		var liveMatch = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		var rounds = await _byMatch.GetOrAddAsync(liveMatch.Id, async _ =>
		{
			await using var connection = await OpenAsync(cancellationToken);
			var rows = await connection.QueryAsync<RoundRow>(
				"""
				SELECT Number, BeatmapHash, Mode, Mods, Freemods, TeamType, WinCondition, Seed, StartedAt, EndedAt, Aborted
				FROM Rounds
				WHERE MatchId = @MatchId
				ORDER BY Number
				""",
				new { MatchId = liveMatch.Id });
			var result = ImmutableList.CreateBuilder<Round>();
			foreach (var row in rows)
				result.Add(Track(ToRound(liveMatch, row)));
			return result.ToImmutable();
		});

		return rounds ?? ImmutableList<Round>.Empty;
	}

	protected override async Task<Round?> ReadAsync(SqliteConnection connection,
		(int MatchId, int Number) key, CancellationToken cancellationToken)
	{
		var match = await matches.GetAsync(key.MatchId, cancellationToken);
		if (match is null)
			return null;

		var row = await connection.QuerySingleOrDefaultAsync<RoundRow>(
			"""
			SELECT Number, BeatmapHash, Mode, Mods, Freemods, TeamType, WinCondition, Seed, StartedAt, EndedAt, Aborted
			FROM Rounds
			WHERE MatchId = @MatchId AND Number = @Number
			""",
			new { key.MatchId, key.Number });
		return row is null ? null : ToRound(match, row);
	}

	protected override Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, Round round)
	{
		return connection.ExecuteAsync(
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
			}, transaction);
	}

	private static ImmutableList<Round> AddOrReplace(ImmutableList<Round> current, Round round)
	{
		var index = current.FindIndex(item => item.Number == round.Number);
		var next = index < 0 ? current.Add(round) : current.SetItem(index, round);
		return next.Sort(static (left, right) => left.Number.CompareTo(right.Number));
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
