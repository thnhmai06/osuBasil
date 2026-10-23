using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Multiplayer;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class PostgresRoundRepository(
	DatabaseReader reader,
	DatabaseWriter writer) : IRoundRepository
{
	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
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
		await writer.SaveAsync(Root.Match(row.MatchId), (GetType(), row.MatchId, row.Number), new WriteCommand((context, token) =>
			context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number, token)));
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var rows = await reader.ReadAsync(context => context.Rounds
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Number)
			.ToListAsync(cancellationToken), cancellationToken);
		return [.. rows.Select(row => ToRound(match, row))];
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