using System.Collections.Immutable;
using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Memory;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class PostgresRoundRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IMatchRepository matches)
	: MemoryRepository<(int MatchId, int Index), Round>(reader, writer), IRoundRepository
{
	private readonly OwnedLists<int, MatchRecord, Round> _byMatch = new();

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		var saved = SaveAsync(round);
		var live = Track(round);
		_byMatch.Change(live.Match.Id, live.Match, current => AddOrReplace(current, live), saved);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var liveMatch = await matches.GetAsync(match.Id, cancellationToken) ?? match;
		return await _byMatch.GetOrLoadAsync(liveMatch.Id, liveMatch, async () =>
		{
			var rows = await reader.ReadAsync(context => context.Rounds
				.Where(row => row.MatchId == liveMatch.Id)
				.OrderBy(row => row.Number)
				.ToListAsync(cancellationToken), cancellationToken);
			var result = ImmutableList.CreateBuilder<Round>();
			foreach (var row in rows)
				result.Add(Track(ToRound(liveMatch, row)));
			return result.ToImmutable();
		});
	}

	protected override (int MatchId, int Index) KeyOf(Round item)
	{
		return (item.Match.Id, item.Index);
	}

	protected override Root RootOf(Round item)
	{
		return Root.Match(item.Match.Id);
	}

	protected override void CopyTo(Round live, Round from)
	{
		live.EndedAt = from.EndedAt;
		live.Aborted = from.Aborted;
	}

	protected override async Task<Round?> LoadAsync((int MatchId, int Index) key)
	{
		var row = await Reader.ReadAsync(context => context.Rounds
			.Where(row => row.MatchId == key.MatchId && row.Number == key.Index)
			.FirstOrDefaultAsync(cancellationToken: CancellationToken.None));
		if (row is null)
			return null;

		var match = await matches.GetAsync(key.MatchId);
		return match is null ? null : ToRound(match, row);
	}

	protected override WriteCommand SaveCommand(Round round)
	{
		var row = new RoundRow
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
			Aborted = round.Aborted
		};
		return new WriteCommand((context, token) =>
			context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number, token));
	}

	private static ImmutableList<Round> AddOrReplace(ImmutableList<Round> current, Round round)
	{
		var index = current.FindIndex(item => item.Index == round.Index);
		var next = index < 0 ? current.Add(round) : current.SetItem(index, round);
		return next.Sort(static (left, right) => left.Index.CompareTo(right.Index));
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
}