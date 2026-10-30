using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Memory;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Multiplayer.Persistence;

/// <summary>Stores matches.</summary>
internal sealed class PostgresMatchRepository(DatabaseReader reader, DatabaseWriter writer, IUserRepository users)
	: MemoryRepository<int, MatchRecord>(reader, writer), IMatchRepository
{
	/// <inheritdoc />
	public async Task<MatchRecord> CreateAsync(MatchData data, CancellationToken cancellationToken = default)
	{
		var value = await WithCreatorAsync(data, cancellationToken);
		var name = value.Name;
		var creatorId = value.Creator.Id;
		var startedAt = value.StartedAt.ToUniversalTime();
		var endedAt = value.EndedAt?.ToUniversalTime();
		var isPrivate = value.IsPrivate;
		var id = await Writer.WriteAsync(Root.Server, async (MultiplayerDbContext context, CancellationToken token) =>
		{
			var row = new MatchRow
			{
				Name = name,
				CreatorId = creatorId,
				StartedAt = startedAt,
				EndedAt = endedAt,
				IsPrivate = isPrivate
			};
			context.Matches.Add(row);
			await context.SaveChangesAsync(token);
			return row.Id;
		}, cancellationToken);

		return Track(new MatchRecord { Id = id, Value = value });
	}

	/// <inheritdoc />
	public Task UpdateAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		_ = SaveAsync(match);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<MatchRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return FindAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<Page<MatchRecord>> ListAsync(MatchQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await Reader.ReadAsync(async (MultiplayerDbContext context) =>
		{
			var matching = Filter(context, context.Matches, query);
			var count = await matching.CountAsync(cancellationToken);
			var rows = await matching
				.OrderByDescending(row => row.StartedAt)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		var matches = new List<MatchRecord>();
		foreach (var row in rows)
			matches.Add(Track(await ToMatchAsync(row, cancellationToken)));

		return new Page<MatchRecord>(matches, total);
	}

	/// <inheritdoc />
	public async Task<bool> IsVisibleToAsync(MatchRecord match, UserRecord viewer,
		CancellationToken cancellationToken = default)
	{
		return await Reader.ReadAsync((MultiplayerDbContext context) =>
			WhereVisibleTo(context, context.Matches.Where(row => row.Id == match.Id), viewer.Id)
				.AnyAsync(cancellationToken), cancellationToken);
	}

	protected override int KeyOf(MatchRecord item)
	{
		return item.Id;
	}

	protected override Root RootOf(MatchRecord item)
	{
		return Root.Match(item.Id);
	}

	protected override void CopyTo(MatchRecord live, MatchRecord from)
	{
		live.Value.Name = from.Value.Name;
		live.Value.EndedAt = from.Value.EndedAt;
		live.Value.IsPrivate = from.Value.IsPrivate;
	}

	protected override async Task<MatchRecord?> LoadAsync(int key)
	{
		var row = await Reader.ReadAsync((MultiplayerDbContext context) => context.Matches
			.Where(row => row.Id == key)
			.FirstOrDefaultAsync(CancellationToken.None));
		return row is null ? null : await ToMatchAsync(row, CancellationToken.None);
	}

	protected override WriteCommand<MultiplayerDbContext> SaveCommand(MatchRecord match)
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
		return new WriteCommand<MultiplayerDbContext>((context, token) =>
			context.ReplaceAsync(row, stored => stored.Id == row.Id, token));
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

	private async Task<MatchRecord> ToMatchAsync(MatchRow row, CancellationToken cancellationToken)
	{
		var creator = (row.CreatorId is { } creatorId
			? await users.GetAsync(creatorId, cancellationToken)
			: null) ?? throw new InvalidDataException($"Stored match {row.Id} has no existing creator.");
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

	private async Task<MatchData> WithCreatorAsync(MatchData data, CancellationToken cancellationToken)
	{
		var creator = await users.GetAsync(data.Creator.Id, cancellationToken) ?? data.Creator;
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