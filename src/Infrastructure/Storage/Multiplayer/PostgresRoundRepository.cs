using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Match;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Match;
using Basil.Domain.Multiplayer.Round;
using Basil.Domain.Utilities;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;

namespace Basil.Infrastructure.Storage.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class PostgresRoundRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IMatchRepository matches)
	: MemoryRepository<(int MatchId, int Number), Round>(reader, writer), IRoundRepository
{
	private readonly OwnedLists<int, Match, Round> _byMatch = new();

	protected override string WriteSql =>
		"""
		insert into rounds (match_id, number, beatmap_hash, mode, mods, freemods, team_type, win_condition, seed, started_at, ended_at, aborted)
		values (@MatchId, @Number, @BeatmapHash, @Mode, @Mods, @Freemods, @TeamType, @WinCondition, @Seed, @StartedAt, @EndedAt, @Aborted)
		on conflict (match_id, number) do update set
			beatmap_hash = excluded.beatmap_hash,
			mode = excluded.mode,
			mods = excluded.mods,
			freemods = excluded.freemods,
			team_type = excluded.team_type,
			win_condition = excluded.win_condition,
			seed = excluded.seed,
			started_at = excluded.started_at,
			ended_at = excluded.ended_at,
			aborted = excluded.aborted;
		""";

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		var saved = SaveAsync(round);
		var live = Track(round);
		_byMatch.Change(live.Match.Id, live.Match, current => AddOrReplace(current, live), saved);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(Match match, CancellationToken cancellationToken = default)
	{
		var liveMatch = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		return await _byMatch.GetOrLoadAsync(liveMatch.Id, liveMatch, async () =>
		{
			var rows = await Reader.ReadAsync(connection => connection.QueryAsync<RoundRow>(
				"""
				select number, beatmap_hash, mode, mods, freemods, team_type, win_condition, seed, started_at, ended_at, aborted
				from rounds
				where match_id = @MatchId
				order by number
				""",
				new { MatchId = liveMatch.Id }), cancellationToken);
			var result = ImmutableList.CreateBuilder<Round>();
			foreach (var row in rows)
				result.Add(Track(ToRound(liveMatch, row)));
			return result.ToImmutable();
		});
	}

	protected override (int MatchId, int Number) KeyOf(Round item)
	{
		return (item.Match.Id, item.Number);
	}

	protected override Root RootOf(Round item)
	{
		return Root.Match(item.Match.Id);
	}

	protected override async Task<Round?> LoadAsync((int MatchId, int Number) key)
	{
		var row = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<RoundRow>(
			"""
			select number, beatmap_hash, mode, mods, freemods, team_type, win_condition, seed, started_at, ended_at, aborted
			from rounds
			where match_id = @MatchId and number = @Number
			""",
			new { key.MatchId, key.Number }));
		if (row is null)
			return null;

		var match = await matches.GetAsync(key.MatchId);
		return match is null ? null : ToRound(match, row);
	}

	protected override void CopyTo(Round live, Round from)
	{
		live.EndedAt = from.EndedAt;
		live.Aborted = from.Aborted;
	}

	protected override object WriteParameters(Round round)
	{
		return new
		{
			MatchId = round.Match.Id,
			round.Number,
			BeatmapHash = round.BeatmapHash.HashValue,
			Mode = (int)round.Settings.Mode,
			Mods = (int)round.Settings.Mods,
			round.Settings.Freemods,
			TeamType = (int)round.Settings.TeamType,
			WinCondition = (int)round.Settings.WinCondition,
			round.Settings.Seed,
			StartedAt = round.StartedAt.ToUniversalTime(),
			EndedAt = round.EndedAt?.ToUniversalTime(),
			round.Aborted
		};
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
			Freemods = row.Freemods,
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
			StartedAt = row.StartedAt,
			EndedAt = row.EndedAt,
			Aborted = row.Aborted
		};
	}

	/// <summary>A stored row of the <c>rounds</c> table.</summary>
	private sealed class RoundRow
	{
		public int Number { get; set; }
		public string BeatmapHash { get; set; } = "";
		public int Mode { get; set; }
		public int Mods { get; set; }
		public bool Freemods { get; set; }
		public int TeamType { get; set; }
		public int WinCondition { get; set; }
		public int Seed { get; set; }
		public DateTimeOffset StartedAt { get; set; }
		public DateTimeOffset? EndedAt { get; set; }
		public bool Aborted { get; set; }
	}
}