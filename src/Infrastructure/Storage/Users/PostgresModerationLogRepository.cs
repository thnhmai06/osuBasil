using System.Collections.Immutable;
using Basil.Application.Storage.Common;
using Basil.Application.Storage.Users;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

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
		var parameters = new
		{
			Action = (int)entry.Action,
			ById = entry.By.Id,
			TargetId = entry.Target.Id,
			entry.Reason,
			Timestamp = entry.Timestamp.ToUniversalTime()
		};
		_ = writer.AppendAsync(Root.User(entry.Target.Id), new WriteCommand(
			"""
			insert into moderation_log (action, by_id, target_id, reason, timestamp)
			values (@Action, @ById, @TargetId, @Reason, @Timestamp)
			""",
			parameters));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask<Page<ModerationRecord>> ListAsync(UserRecord target, Interval<DateTimeOffset>? within,
		PageRequest page, CancellationToken cancellationToken = default)
	{
		var liveTarget = await users.GetAsync(target.Id, cancellationToken) ?? target;
		var parameters = new DynamicParameters(new { TargetId = liveTarget.Id, page.Limit, page.Offset });
		var conditions = new List<string> { "target_id = @TargetId" };
		if (within is { Min: { } min })
		{
			parameters.Add("MinTimestamp", min.ToUniversalTime());
			conditions.Add(within.Value.MinInclusive ? "timestamp >= @MinTimestamp" : "timestamp > @MinTimestamp");
		}

		if (within is { Max: { } max })
		{
			parameters.Add("MaxTimestamp", max.ToUniversalTime());
			conditions.Add(within.Value.MaxInclusive ? "timestamp <= @MaxTimestamp" : "timestamp < @MaxTimestamp");
		}

		var where = string.Join(" and ", conditions);
		var (total, rows) = await reader.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>(
				$"select count(*) from moderation_log where {where}", parameters),
			await connection.QueryAsync<ModerationLogRow>(
				$"""
				 select id, action, by_id, target_id, reason, timestamp
				 from moderation_log
				 where {where}
				 order by timestamp desc, id desc
				 limit @Limit offset @Offset
				 """,
				parameters)), cancellationToken);

		var entries = ImmutableList.CreateBuilder<ModerationRecord>();
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

			entries.Add(new ModerationRecord
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

		return new Page<ModerationRecord>(entries.ToImmutable(), total);
	}

	/// <summary>A stored row of the <c>moderation_log</c> table.</summary>
	private sealed class ModerationLogRow
	{
		public long Id { get; set; }
		public int Action { get; set; }
		public int ById { get; set; }
		public int TargetId { get; set; }
		public string? Reason { get; set; }
		public DateTimeOffset Timestamp { get; set; }
	}
}