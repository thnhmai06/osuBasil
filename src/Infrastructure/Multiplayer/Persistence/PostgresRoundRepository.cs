using System.Text.Json;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class PostgresRoundRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IMatchRepository matches)
	: MemoryRepository<(int MatchId, int Index), Round>(reader, writer), IRoundRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(round);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var rows = await Reader.ReadAsync((MultiplayerDbContext context) => context.Rounds
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Number)
			.ToListAsync(cancellationToken), cancellationToken);
		var stored = rows.Select(row => Track(ToRound(match, row))).ToList();

		// A round whose first write is still queued is not in the database yet, but it is live here.
		var pending =
			Items.Values.Where(round => round.Match.Id == match.Id && stored.All(s => s.Index != round.Index));
		return [.. stored.Concat(pending).OrderBy(round => round.Index)];
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
		live.Decision = from.Decision;
		live.Result = from.Result;
	}

	protected override async Task<Round?> LoadAsync((int MatchId, int Index) key)
	{
		var row = await Reader.ReadAsync((MultiplayerDbContext context) => context.Rounds
			.Where(row => row.MatchId == key.MatchId && row.Number == key.Index)
			.FirstOrDefaultAsync(CancellationToken.None));
		if (row is null)
			return null;

		var match = await matches.GetAsync(key.MatchId);
		return match is null ? null : ToRound(match, row);
	}

	protected override WriteCommand<MultiplayerDbContext> SaveCommand(Round round)
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
			Aborted = round.Aborted,
			Result = round.Decision is { } decision ? JsonSerializer.Serialize(decision) : null
		};
		return new WriteCommand<MultiplayerDbContext>((context, token) =>
			context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number, token));
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
			Aborted = row.Aborted,
			Decision = row.Result is null ? null : JsonSerializer.Deserialize<RoundVerdict>(row.Result)
		};
	}
}