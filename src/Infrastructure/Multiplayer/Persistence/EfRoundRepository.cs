using System.Text.Json;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores the rounds played in matches.</summary>
internal sealed class EfRoundRepository(DatabaseSession database) : IRoundRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Round round, CancellationToken cancellationToken = default)
	{
		var row = ToRow(round);
		return database.WriteAsync((MultiplayerDbContext context, CancellationToken token) =>
			context.UpsertAsync(row, stored => stored.MatchId == row.MatchId && stored.Number == row.Number, token),
			cancellationToken);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((MultiplayerDbContext context) => context.Rounds
			.Where(row => row.MatchId == match.Id)
			.OrderBy(row => row.Number)
			.ToListAsync(cancellationToken), cancellationToken);
		return [.. rows.Select(row => ToRound(match, row))];
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
			Result = round.Decision is { } decision ? JsonSerializer.Serialize(decision) : null
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
			Aborted = row.Aborted,
			Decision = row.Result is null ? null : JsonSerializer.Deserialize<RoundVerdict>(row.Result)
		};
	}
}