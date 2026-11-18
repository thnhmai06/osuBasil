using System.Net;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

internal sealed class EfConnectionRepository(DatabaseSession database) : IConnectionRepository
{
	public Task OpenAsync(long sessionId, IPAddress ip, string? clientInfo, DateTimeOffset at, TimeSpan reopenWithin)
	{
		var opened = at.ToUniversalTime();
		var earliest = opened - reopenWithin;
		return database.WriteAsync(async (AuthenticationDbContext context) =>
		{
			var last = await context.Connections.AsTracking().Where(row => row.SessionId == sessionId)
				.OrderByDescending(row => row.OpenedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync();
			if (last is { ClosedAt: { } closedAt } && closedAt >= earliest && Equals(last.Ip, ip))
			{
				last.ClosedAt = null;
				last.CloseReason = null;
				last.LastActiveAt = opened;
				return;
			}

			context.Connections.Add(new ConnectionRow
			{
				Id = await IdCounters.NextAsync(context, "connection"),
				SessionId = sessionId,
				Ip = ip,
				ClientInfo = clientInfo,
				OpenedAt = opened,
				LastActiveAt = opened
			});
		});
	}

	public Task CloseAsync(long sessionId, DateTimeOffset lastActiveAt, DateTimeOffset closedAt,
		SessionEndReason reason)
	{
		var active = lastActiveAt.ToUniversalTime();
		var closed = closedAt.ToUniversalTime();
		return database.WriteAsync((AuthenticationDbContext context) => context.Connections
			.Where(row => row.SessionId == sessionId && row.ClosedAt == null)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.LastActiveAt, active)
				.SetProperty(row => row.ClosedAt, closed)
				.SetProperty(row => row.CloseReason, reason)));
	}

	public Task WriteActivityAsync(IReadOnlyCollection<ConnectionActivity> activity)
	{
		if (activity.Count == 0) return Task.CompletedTask;

		return database.TransactAsync(async (AuthenticationDbContext context) =>
		{
			foreach (var item in activity)
			{
				var last = item.LastActiveAt.ToUniversalTime();
				await context.Connections.Where(row => row.SessionId == item.SessionId && row.ClosedAt == null)
					.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastActiveAt, last));
			}
		});
	}

	public async ValueTask<Page<ConnectionRecord>> ListAsync(long sessionId, PageRequest page,
		CancellationToken ct = default)
	{
		var fetched = await database.ReadAsync((AuthenticationDbContext context) => context.Connections
			.Where(row => row.SessionId == sessionId)
			.OrderByDescending(row => row.OpenedAt)
			.ThenByDescending(row => row.Id)
			.Skip(page.Offset)
			.Take(page.Fetch)
			.ToListAsync(ct), ct);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<ConnectionRecord>(rows.Select(ToRecord).ToList(), hasNext);
	}

	public Task<int> CloseOpenAsync(SessionEndReason reason)
	{
		return database.WriteAsync((AuthenticationDbContext context) => context.Connections
			.Where(row => row.ClosedAt == null)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.ClosedAt, row => row.LastActiveAt)
				.SetProperty(row => row.CloseReason, reason)));
	}

	private static ConnectionRecord ToRecord(ConnectionRow row)
	{
		return new ConnectionRecord
		{
			Id = row.Id,
			Value = new ConnectionData(row.SessionId, row.Ip, row.ClientInfo, row.OpenedAt, row.LastActiveAt,
				row.ClosedAt, row.CloseReason)
		};
	}
}