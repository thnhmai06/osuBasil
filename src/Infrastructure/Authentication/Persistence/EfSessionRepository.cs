using System.Globalization;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>Records the history of sessions.</summary>
internal sealed class EfSessionRepository(DatabaseSession database) : ISessionRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(SessionRecord session)
	{
		var row = ToRow(session);
		return database.WriteAsync(async (AuthenticationDbContext context) =>
		{
			var stored = await context.Sessions.AsTracking().FirstOrDefaultAsync(item => item.Id == row.Id);
			if (stored is null) context.Sessions.Add(row);
			else context.Entry(stored).CurrentValues.SetValues(row);
		});
	}

	/// <inheritdoc />
	public async ValueTask<Page<SessionRecord>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken ct = default)
	{
		var user = query.User;
		var types = query.Types;
		var now = query.Now.ToUniversalTime();
		var fetched = await database.ReadAsync(async (AuthenticationDbContext context) =>
		{
			var sessions = context.Sessions.Where(row => row.UserId == user.Id);
			if (types is not null) sessions = sessions.Where(row => types.Contains(row.Type));
			sessions = query.State switch
			{
				SessionState.Active => sessions.Where(row =>
					row.EndedAt == null && (row.ExpiresAt == null || row.ExpiresAt > now)),
				SessionState.Ended => sessions.Where(row =>
					row.EndedAt != null || (row.ExpiresAt != null && row.ExpiresAt <= now)),
				_ => sessions
			};
			return await sessions
				.OrderByDescending(row => row.StartedAt)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Fetch)
				.ToListAsync(ct);
		}, ct);

		var (rows, hasNext) = page.Slice(fetched);
		return new Page<SessionRecord>(rows.Select(row => ToRecord(row, user)).ToList(), hasNext);
	}

	/// <inheritdoc />
	public async Task<SessionRecord?> GetAsync(UserRecord user, long id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, ct),
			ct);
		return row is null ? null : ToRecord(row, user);
	}

	/// <inheritdoc />
	public async Task<StoredSession?> FindByTokenAsync(SessionType type, Sha256 tokenHash, DateTimeOffset now,
		CancellationToken ct = default)
	{
		var at = now.ToUniversalTime();
		var hash = Convert.FromHexString(tokenHash.HashValue);
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.TokenHash == hash && item.Type == type && item.EndedAt == null &&
				                             (item.ExpiresAt == null || item.ExpiresAt > at), ct),
			ct);
		return row is null
			? null
			: new StoredSession(row.Id, row.UserId, row.Type, row.Ip, row.ClientInfo, row.StartedAt, row.ExpiresAt);
	}

	/// <inheritdoc />
	public Task EndAsync(UserRecord user, long? only, long? except, IReadOnlyList<SessionType> types,
		DateTimeOffset at, SessionEndReason reason)
	{
		var end = at.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context) =>
		{
			var query = context.Sessions.Where(row => row.UserId == user.Id && types.Contains(row.Type));
			if (only is { } one) query = query.Where(row => row.Id == one);
			if (except is { } kept) query = query.Where(row => row.Id != kept);

			await query.Where(row => row.EndedAt == null).ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, reason));
		});
	}

	/// <inheritdoc />
	public Task<int> EndOpenAsync(IReadOnlyList<SessionType> types, SessionEndReason reason)
	{
		return database.WriteAsync((AuthenticationDbContext context) => context.Sessions
			.Where(row => row.EndedAt == null && types.Contains(row.Type))
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, row => row.LastActiveAt)
				.SetProperty(row => row.EndReason, reason)));
	}

	/// <inheritdoc />
	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity)
	{
		if (activity.Count == 0) return Task.CompletedTask;

		return database.TransactAsync(async (AuthenticationDbContext context) =>
		{
			foreach (var item in activity)
			{
				var last = item.LastActiveAt.ToUniversalTime();
				var expires = item.ExpiresAt?.ToUniversalTime();
				await context.Sessions.Where(row => row.Id == item.Id && row.EndedAt == null)
					.ExecuteUpdateAsync(setters => setters
						.SetProperty(row => row.LastActiveAt, last)
						.SetProperty(row => row.ExpiresAt, expires));
			}
		});
	}

	/// <inheritdoc />
	public Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff)
	{
		var before = cutoff.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context) =>
			await context.Sessions.Where(row => (row.EndedAt != null && row.EndedAt < before) ||
			                                    (row.EndedAt == null && row.ExpiresAt != null &&
			                                     row.ExpiresAt < before))
				.ExecuteDeleteAsync());
	}

	private static SessionRecord ToRecord(SessionRow row, UserRecord user)
	{
		var end = row.EndedAt is { } at ? new SessionEnd(at, row.EndReason!.Value) : null;
		return new SessionRecord
		{
			Id = row.Id,
			Value = new SessionData(user, row.Type, row.StartedAt,
				new SessionFingerprint(row.Ip, ToClient(row), row.ClientInfo),
				new Sha256(Convert.ToHexStringLower(row.TokenHash)), row.ExpiresAt, row.LastActiveAt, end)
		};
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
		var data = session.Value;
		var device = data.Fingerprint;
		var client = device.Client;
		return new SessionRow
		{
			Id = session.Id,
			UserId = data.User.Id,
			Type = data.Type,
			Ip = device.Ip,
			ClientInfo = device.ClientInfo,
			StartedAt = data.StartedAt.ToUniversalTime(),
			LastActiveAt = data.LastActiveAt.ToUniversalTime(),
			EndedAt = data.End?.At.ToUniversalTime(),
			EndReason = data.End?.Reason,
			TokenHash = Convert.FromHexString(data.TokenHash.HashValue),
			ExpiresAt = data.ExpiresAt?.ToUniversalTime(),
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