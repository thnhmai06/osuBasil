using System.Globalization;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>Records the history of sessions.</summary>
internal sealed class EfSessionRepository(DatabaseSession database) : ISessionRepository
{
	/// <inheritdoc />
	public Task RecordAsync(SessionRecord login, CancellationToken cancellationToken = default)
	{
		var row = ToRow(login);

		return database.WriteAsync((AuthenticationDbContext context, CancellationToken _) =>
		{
			context.Sessions.Add(row);
			return Task.CompletedTask;
		}, cancellationToken);
	}

	/// <inheritdoc />
	public Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default)
	{
		var row = ToRow(session);
		return database.WriteAsync((AuthenticationDbContext context, CancellationToken token) =>
			context.ReplaceAsync(row, item => item.Id == row.Id, token), cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<Page<SessionRecord>> ListAsync(UserRecord user, SessionFilter filter, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var types = filter.Types;
		var now = filter.Now.ToUniversalTime();
		var (total, rows) = await database.ReadAsync(async (AuthenticationDbContext context) =>
		{
			var query = context.Sessions.Where(row => row.UserId == user.Id);
			if (types is not null) query = query.Where(row => types.Contains(row.Type));
			query = filter.State switch
			{
				SessionState.Active => query.Where(row =>
					row.EndedAt == null && (row.ExpiresAt == null || row.ExpiresAt > now)),
				SessionState.Ended => query.Where(row =>
					row.EndedAt != null || (row.ExpiresAt != null && row.ExpiresAt <= now)),
				_ => query
			};
			var count = await query.CountAsync(cancellationToken);
			var rows = await query
				.OrderByDescending(row => row.StartedAt)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		return new Page<SessionRecord>(rows.Select(row => ToRecord(row, user)).ToList(), total);
	}

	/// <inheritdoc />
	public async Task<SessionRecord?> GetAsync(UserRecord user, Guid id, CancellationToken cancellationToken = default)
	{
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, cancellationToken),
			cancellationToken);
		return row is null ? null : ToRecord(row, user);
	}

	/// <inheritdoc />
	public async Task<StoredSession?> FindByTokenAsync(SessionType type, byte[] tokenHash, DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		var at = now.ToUniversalTime();
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.Type == type && item.EndedAt == null &&
				                             (item.ExpiresAt == null || item.ExpiresAt > at), cancellationToken),
			cancellationToken);
		return row is null
			? null
			: new StoredSession(row.Id, row.UserId, row.Type, row.Ip, row.ClientInfo, row.StartedAt, row.ExpiresAt);
	}

	/// <inheritdoc />
	public Task EndAsync(UserRecord user, Guid? only, Guid? except, IReadOnlyList<SessionType> types,
		DateTimeOffset at, SessionEndReason reason, CancellationToken cancellationToken = default)
	{
		var end = at.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context, CancellationToken token) =>
		{
			var query = context.Sessions.Where(row => row.UserId == user.Id && types.Contains(row.Type));
			if (only is { } one) query = query.Where(row => row.Id == one);
			if (except is { } kept) query = query.Where(row => row.Id != kept);

			await query.Where(row => row.EndedAt == null).ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, reason), token);
		}, cancellationToken);
	}

	/// <inheritdoc />
	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity,
		CancellationToken cancellationToken = default)
	{
		if (activity.Count == 0) return Task.CompletedTask;

		return database.TransactAsync(async (AuthenticationDbContext context, CancellationToken token) =>
		{
			foreach (var item in activity)
			{
				var last = item.LastActiveAt.ToUniversalTime();
				var expires = item.ExpiresAt?.ToUniversalTime();
				await context.Sessions.Where(row => row.Id == item.Id && row.EndedAt == null)
					.ExecuteUpdateAsync(setters => setters
						.SetProperty(row => row.LastActiveAt, last)
						.SetProperty(row => row.ExpiresAt, expires), token);
			}
		}, cancellationToken);
	}

	/// <inheritdoc />
	public Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
	{
		var before = cutoff.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context, CancellationToken token) =>
			await context.Sessions.Where(row => (row.EndedAt != null && row.EndedAt < before) ||
			                                    (row.EndedAt == null && row.ExpiresAt != null &&
			                                     row.ExpiresAt < before))
				.ExecuteDeleteAsync(token), cancellationToken);
	}

	private static SessionRecord ToRecord(SessionRow row, UserRecord user)
	{
		var end = row.EndedAt is { } at ? new SessionEnd(at, row.EndReason!.Value) : null;
		return new SessionRecord(row.Id, user, row.Type, row.StartedAt,
			new SessionData(new SessionDevice(row.Ip, ToClient(row), row.ClientInfo),
				new SessionCredential(row.TokenHash, row.ExpiresAt), row.LastActiveAt, end));
	}

	private static OsuClient? ToClient(SessionRow row)
	{
		if (row.ClientDate is null) return null;

		var version = new OsuVersion(DateOnly.ParseExact(row.ClientDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
			row.ClientRevision, (OsuVersionStream)row.ClientStream!.Value);
		var adapters = new NetworkAdapters
			{ Adapters = row.NetworkAdapters!, Hash = new Md5(row.NetworkAdaptersHash!) };
		return new OsuClient(version,
			new OsuFingerprint(new Md5(row.OsuPathHash!), new Md5(row.UninstallHash!), new Md5(row.DiskSignatureHash!),
				adapters));
	}

	private static SessionRow ToRow(SessionRecord session)
	{
		var (device, credential, lastActiveAt, end) = session.Value;
		var client = device.Client;
		return new SessionRow
		{
			Id = session.Id,
			UserId = session.User.Id,
			Type = session.Type,
			Ip = device.Ip,
			ClientInfo = device.ClientInfo,
			StartedAt = session.StartedAt.ToUniversalTime(),
			LastActiveAt = lastActiveAt.ToUniversalTime(),
			EndedAt = end?.At.ToUniversalTime(),
			EndReason = end?.Reason,
			TokenHash = credential.TokenHash is null ? null : [.. credential.TokenHash],
			ExpiresAt = credential.ExpiresAt?.ToUniversalTime(),
			ClientDate = client?.Version.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			ClientRevision = client?.Version.Revision,
			ClientStream = client is null ? null : (int?)client.Version.Stream,
			OsuPathHash = client?.Fingerprint.PathHash.HashValue,
			NetworkAdapters = client?.Fingerprint.NetworkAdapters.Adapters,
			NetworkAdaptersHash = client?.Fingerprint.NetworkAdapters.Hash.HashValue,
			UninstallHash = client?.Fingerprint.UninstallHash.HashValue,
			DiskSignatureHash = client?.Fingerprint.DiskSignatureHash.HashValue
		};
	}
}