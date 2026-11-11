using Basil.Application.Shared;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores matches.</summary>
internal sealed class EfMatchRepository(DatabaseSession database, IUserRepository users) : IMatchRepository
{
	/// <inheritdoc />
	public async Task<MatchRecord> CreateAsync(MatchData data)
	{
		var value = await WithCreatorAsync(data);
		var name = value.Name;
		var creatorId = value.Creator.Id;
		var startedAt = value.StartedAt.ToUniversalTime();
		var endedAt = value.EndedAt?.ToUniversalTime();
		var isPrivate = value.IsPrivate;
		var row = new MatchRow
		{
			Name = name,
			CreatorId = creatorId,
			StartedAt = startedAt,
			EndedAt = endedAt,
			IsPrivate = isPrivate
		};
		await database.WriteAsync(async (MultiplayerDbContext context) =>
		{
			row.Id = (int)await IdCounters.NextAsync(context, "match");
			context.Matches.Add(row);
		});

		return new MatchRecord { Id = row.Id, Value = value };
	}

	/// <inheritdoc />
	public Task UpdateAsync(MatchRecord match)
	{
		var row = new MatchRow
		{
			Id = match.Id,
			Name = match.Value.Name,
			CreatorId = match.Value.Creator.Id,
			StartedAt = match.Value.StartedAt.ToUniversalTime(),
			EndedAt = match.Value.EndedAt?.ToUniversalTime(),
			IsPrivate = match.Value.IsPrivate
		};
		return database.WriteAsync((MultiplayerDbContext context) =>
			context.ReplaceAsync(row, stored => stored.Id == row.Id));
	}

	/// <inheritdoc />
	public async ValueTask<MatchRecord?> GetAsync(int id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((MultiplayerDbContext context) => context.Matches
			.Where(item => item.Id == id)
			.FirstOrDefaultAsync(ct), ct);
		return row is null ? null : await ToMatchAsync(row, ct);
	}

	/// <inheritdoc />
	public async Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken ct = default)
	{
		var fetched = await database.ReadAsync((MultiplayerDbContext context) => Filter(context, context.Matches, query)
			.OrderByDescending(row => row.StartedAt)
			.ThenByDescending(row => row.Id)
			.Skip(page.Offset)
			.Take(page.Fetch)
			.ToListAsync(ct), ct);

		var (rows, hasNext) = page.Slice(fetched);
		var matches = new List<MatchRecord>();
		foreach (var row in rows)
			matches.Add(await ToMatchAsync(row, ct));

		return new Page<MatchRecord>(matches, hasNext);
	}

	/// <inheritdoc />
	public async Task<bool> IsVisibleToAsync(MatchRecord match, UserRecord viewer,
		CancellationToken ct = default)
	{
		return await database.ReadAsync((MultiplayerDbContext context) =>
			WhereVisibleTo(context, context.Matches.Where(row => row.Id == match.Id), viewer.Id)
				.AnyAsync(ct), ct);
	}

	/// <summary>Narrows the matches to the ones a query asks for.</summary>
	private static IQueryable<MatchRow> Filter(MultiplayerDbContext context, IQueryable<MatchRow> matches,
		MatchQuery query)
	{
		if (query.Ended is { } ended)
			matches = ended ? matches.Where(row => row.EndedAt != null) : matches.Where(row => row.EndedAt == null);
		if (query.Private is { } isPrivate)
			matches = matches.Where(row => row.IsPrivate == isPrivate);
		if (query.VisibleTo is { } viewer)
			matches = WhereVisibleTo(context, matches, viewer);
		return matches;
	}

	/// <summary>Keeps the public matches and the private ones the user took part in or manages.</summary>
	private static IQueryable<MatchRow> WhereVisibleTo(MultiplayerDbContext context, IQueryable<MatchRow> matches,
		int viewer)
	{
		const int refAdded = (int)MatchEventType.RefAdded;
		const int refRemoved = (int)MatchEventType.RefRemoved;
		const int playerJoined = (int)MatchEventType.PlayerJoined;
		return matches.Where(row => !row.IsPrivate
		                            || row.CreatorId == viewer
		                            || context.MatchEvents.Any(added => added.MatchId == row.Id
		                                                                && added.TargetId == viewer
		                                                                && added.Type == refAdded
		                                                                && !context.MatchEvents.Any(removed =>
			                                                                removed.MatchId == row.Id
			                                                                && removed.TargetId == viewer
			                                                                && removed.Type == refRemoved
			                                                                && removed.Id > added.Id))
		                            || context.MatchEvents.Any(joined => joined.MatchId == row.Id
		                                                                 && joined.TargetId == viewer
		                                                                 && joined.Type == playerJoined));
	}

	private async Task<MatchRecord> ToMatchAsync(MatchRow row, CancellationToken ct)
	{
		var creator = await users.GetAsync(row.CreatorId, ct)
		              ?? throw new InvalidDataException($"Stored match {row.Id} has no existing creator.");
		return new MatchRecord
		{
			Id = row.Id,
			Value = new MatchData
			{
				Name = row.Name,
				Creator = creator,
				StartedAt = row.StartedAt,
				EndedAt = row.EndedAt,
				IsPrivate = row.IsPrivate
			}
		};
	}

	private async Task<MatchData> WithCreatorAsync(MatchData data, CancellationToken ct = default)
	{
		var creator = await users.GetAsync(data.Creator.Id, ct) ?? data.Creator;
		return new MatchData
		{
			Name = data.Name,
			Creator = creator,
			StartedAt = data.StartedAt,
			EndedAt = data.EndedAt,
			IsPrivate = data.IsPrivate
		};
	}
}