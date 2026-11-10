using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the moderation history of users.</summary>
internal sealed class EfModerationLogRepository(DatabaseSession database) : IModerationLogRepository
{
	/// <inheritdoc />
	public Task AppendAsync(ModerationData entry)
	{
		return database.WriteAsync((UsersDbContext context) => AddAsync(context, entry));
	}

	/// <inheritdoc />
	public async ValueTask<Page<ModerationLog>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default)
	{
		var targetId = target.Id;
		var minTimestamp = within?.Min?.ToUniversalTime();
		var maxTimestamp = within?.Max?.ToUniversalTime();

		var fetched = await database.ReadAsync(async (UsersDbContext context) =>
		{
			var query = context.ModerationLog.Where(row => row.TargetId == targetId);

			if (minTimestamp is { } min)
				query = within!.Value.MinInclusive
					? query.Where(row => row.Timestamp >= min)
					: query.Where(row => row.Timestamp > min);

			if (maxTimestamp is { } max)
				query = within!.Value.MaxInclusive
					? query.Where(row => row.Timestamp <= max)
					: query.Where(row => row.Timestamp < max);

			var paged = query
				.OrderByDescending(row => row.Timestamp)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Fetch);
			return await (from row in paged
				join actor in context.Users on row.ById equals actor.Id
				orderby row.Timestamp descending, row.Id descending
				select new { row, actor }).ToListAsync(cancellationToken);
		}, cancellationToken);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<ModerationLog>([
			.. rows.Select(found => new ModerationLog
			{
				Id = found.row.Id,
				Value = new ModerationData
				{
					Action = (ModerationAction)found.row.Action,
					Actor = EfUserRepository.ToUser(found.actor),
					Target = target,
					Reason = found.row.Reason,
					Timestamp = found.row.Timestamp
				}
			})
		], hasNext);
	}

	/// <summary>Adds an entry to a context, to be stored with the other changes of that context.</summary>
	internal static async Task AddAsync(UsersDbContext context, ModerationData entry)
	{
		var row = new ModerationLogRow
		{
			Action = (int)entry.Action,
			ById = entry.Actor.Id,
			TargetId = entry.Target.Id,
			Reason = entry.Reason,
			Timestamp = entry.Timestamp.ToUniversalTime()
		};
		row.Id = await IdCounters.NextAsync(context, "moderation_log");
		context.ModerationLog.Add(row);
	}
}