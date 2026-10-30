using System.Collections.Immutable;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the moderation history of users.</summary>
internal sealed class PostgresModerationLogRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IUserRepository users)
	: IModerationLogRepository
{
	/// <inheritdoc />
	public Task AppendAsync(ModerationData entry, CancellationToken cancellationToken = default)
	{
		var action = (int)entry.Action;
		var byId = entry.By.Id;
		var targetId = entry.Target.Id;
		var reason = entry.Reason;
		var timestamp = entry.Timestamp.ToUniversalTime();
		_ = writer.AppendAsync(Root.User(entry.Target.Id),
			new WriteCommand<UsersDbContext>(async (context, token) =>
			{
				context.ModerationLog.Add(new ModerationLogRow
				{
					Action = action,
					ById = byId,
					TargetId = targetId,
					Reason = reason,
					Timestamp = timestamp
				});
				await context.SaveChangesAsync(token);
			}));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask<Page<ModerationLog>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default)
	{
		var liveTarget = await users.GetAsync(target.Id, cancellationToken) ?? target;
		var minTimestamp = within?.Min?.ToUniversalTime();
		var maxTimestamp = within?.Max?.ToUniversalTime();

		var (total, rows) = await reader.ReadAsync(async (UsersDbContext context) =>
		{
			var query = context.ModerationLog.Where(row => row.TargetId == liveTarget.Id);

			if (minTimestamp is { } min)
				query = within!.Value.MinInclusive
					? query.Where(row => row.Timestamp >= min)
					: query.Where(row => row.Timestamp > min);

			if (maxTimestamp is { } max)
				query = within!.Value.MaxInclusive
					? query.Where(row => row.Timestamp <= max)
					: query.Where(row => row.Timestamp < max);

			var count = await query.CountAsync(cancellationToken);
			var rows = await query
				.OrderByDescending(row => row.Timestamp)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		var entries = ImmutableList.CreateBuilder<ModerationLog>();
		foreach (var row in rows)
		{
			var by = await users.GetAsync(row.ById, cancellationToken);
			if (by is null)
				continue;

			var entryTarget = row.TargetId == liveTarget.Id
				? liveTarget
				: await users.GetAsync(row.TargetId, cancellationToken);
			if (entryTarget is null)
				continue;

			entries.Add(new ModerationLog
			{
				Id = row.Id,
				Value = new ModerationData
				{
					Action = (ModerationAction)row.Action,
					By = by,
					Target = entryTarget,
					Reason = row.Reason,
					Timestamp = row.Timestamp
				}
			});
		}

		return new Page<ModerationLog>(entries.ToImmutable(), total);
	}
}