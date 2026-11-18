using System.Globalization;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;

using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Shared.Structures.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

internal sealed class EfSessionRepository(DatabaseSession database) : ISessionRepository
{
	public Task CreateAsync(Session session)
	{
		var row = ToRow(session);
		return database.WriteAsync((AuthenticationDbContext context) =>
		{
			context.Sessions.Add(row);
			return Task.CompletedTask;
		});
	}

	public async ValueTask<Page<Session>> ListAsync(SessionQuery query, PageRequest page,
		CancellationToken ct = default)
	{
		var user = query.User;
		var types = query.Types;
		var fetched = await database.ReadAsync(async (AuthenticationDbContext context) =>
		{
			var sessions = context.Sessions.Where(row => row.UserId == user.Id);
			if (types is not null) sessions = sessions.Where(row => types.Contains(row.Type));
			sessions = query.State switch
			{
				SessionFilter.Active => sessions.Where(row => row.EndedAt == null),
				SessionFilter.Ended => sessions.Where(row => row.EndedAt != null),
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
		return new Page<Session>(rows.Select(row => ToSession(row, user)).ToList(), hasNext);
	}

	public async Task<Session?> GetAsync(UserRecord user, long id, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, ct),
			ct);
		return row is null ? null : ToSession(row, user);
	}

	public async Task<int?> FindOpenOwnerAsync(SessionType type, Sha256Hash tokenHash,
		CancellationToken ct = default)
	{
		var hash = Convert.FromHexString(tokenHash.HashValue);
		return await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.Where(item => item.TokenHash == hash && item.Type == type && item.EndedAt == null)
				.Select(item => (int?)item.UserId)
				.FirstOrDefaultAsync(ct),
			ct);
	}

	public async Task<Session?> FindOpenByTokenAsync(UserRecord user, SessionType type, Sha256Hash tokenHash,
		CancellationToken ct = default)
	{
		var hash = Convert.FromHexString(tokenHash.HashValue);
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.TokenHash == hash && item.Type == type && item.UserId == user.Id &&
				                             item.EndedAt == null, ct),
			ct);
		return row is null ? null : ToSession(row, user);
	}

	public async Task<Session?> FindOpenAsync(UserRecord user, SessionType type, CancellationToken ct = default)
	{
		var row = await database.ReadAsync((AuthenticationDbContext context) => context.Sessions
				.Where(item => item.UserId == user.Id && item.Type == type && item.EndedAt == null)
				.OrderByDescending(item => item.LastActiveAt)
				.FirstOrDefaultAsync(ct),
			ct);
		return row is null ? null : ToSession(row, user);
	}

	public Task EndAsync(long id, SessionEnded ended)
	{
		var end = ended.At.ToUniversalTime();
		return database.WriteAsync((AuthenticationDbContext context) => context.Sessions
			.Where(row => row.Id == id && row.EndedAt == null)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, ended.Reason)));
	}

	public Task EndAsync(UserRecord user, long? only, long? except, SessionEndReason reason, DateTimeOffset at)
	{
		var end = at.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context) =>
		{
			var query = context.Sessions.Where(row => row.UserId == user.Id);
			if (only is { } one) query = query.Where(row => row.Id == one);
			if (except is { } kept) query = query.Where(row => row.Id != kept);

			await query.Where(row => row.EndedAt == null).ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, reason));
		});
	}

	public Task<int> EndOpenAsync(SessionType type, SessionEndReason reason, DateTimeOffset at)
	{
		var end = at.ToUniversalTime();
		return database.WriteAsync((AuthenticationDbContext context) => context.Sessions
			.Where(row => row.EndedAt == null && row.Type == type)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, reason)));
	}

	public Task<int> EndSilentAsync(DateTimeOffset now)
	{
		var end = now.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context) =>
		{
			var ended = 0;
			foreach (var type in Enum.GetValues<SessionType>())
			{
				var silentSince = end - type.ExpiresAfter;
				ended += await context.Sessions
					.Where(row => row.EndedAt == null && row.Type == type && row.LastActiveAt <= silentSince)
					.ExecuteUpdateAsync(setters => setters
						.SetProperty(row => row.EndedAt, end)
						.SetProperty(row => row.EndReason, SessionEndReason.TimedOut));
			}

			return ended;
		});
	}

	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity)
	{
		if (activity.Count == 0) return Task.CompletedTask;

		return database.TransactAsync(async (AuthenticationDbContext context) =>
		{
			foreach (var item in activity)
			{
				var last = item.LastActiveAt.ToUniversalTime();
				// A session already past its expiry stays expired: activity never brings it back.
				var expiredBy = last - item.Type.ExpiresAfter;
				await context.Sessions.Where(row => row.Id == item.Id && row.EndedAt == null &&
				                                    row.LastActiveAt > expiredBy)
					.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastActiveAt, last));
			}
		});
	}

	public Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff)
	{
		var before = cutoff.ToUniversalTime();
		return database.WriteAsync(async (AuthenticationDbContext context) =>
			await context.Sessions.Where(row => row.EndedAt != null && row.EndedAt < before)
				.ExecuteDeleteAsync());
	}

	private static Session ToSession(SessionRow row, UserRecord user)
	{
		var session = new Session
		{
			Id = row.Id,
			User = user,
			Type = row.Type,
			StartedAt = row.StartedAt,
			Fingerprint = new SessionFingerprint(row.Ip, ToClient(row), row.ClientInfo),
			TokenHash = new Sha256Hash(Convert.ToHexStringLower(row.TokenHash)),
			LastActiveAt = row.LastActiveAt
		};
		if (row.EndedAt is { } at) session.End(at, row.EndReason!.Value);
		return session;
	}

	private static OsuClient? ToClient(SessionRow row)
	{
		if (row.ClientDate is null) return null;

		var version = new OsuVersion(DateOnly.ParseExact(row.ClientDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
			row.ClientRevision, (OsuVersionStream)row.ClientStream!.Value);
		var adapters = new NetworkAdapters
			{ Adapters = row.NetworkAdapters!, Hash = new Md5Hash(row.NetworkAdaptersHash!) };
		return new OsuClient(version,
			new OsuFingerprint(new Md5Hash(row.OsuPathHash!), new Md5Hash(row.UninstallHash!), new Md5Hash(row.DiskSignatureHash!),
				adapters));
	}

	private static SessionRow ToRow(Session data)
	{
		var device = data.Fingerprint;
		var client = device.Client;
		return new SessionRow
		{
			Id = data.Id,
			UserId = data.User.Id,
			Type = data.Type,
			Ip = device.Ip,
			ClientInfo = device.ClientInformation,
			StartedAt = data.StartedAt.ToUniversalTime(),
			LastActiveAt = data.LastActiveAt.ToUniversalTime(),
			EndedAt = data.Ended?.At.ToUniversalTime(),
			EndReason = data.Ended?.Reason,
			TokenHash = Convert.FromHexString(data.TokenHash.HashValue),
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