using System.Globalization;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Auth.Persistence;

/// <summary>Records the history of sessions.</summary>
internal sealed class PostgresSessionRepository(DatabaseWriter writer, DatabaseReader reader) : ISessionRepository
{
	/// <inheritdoc />
	public Task RecordAsync(SessionRecord login, CancellationToken cancellationToken = default)
	{
		var row = ToRow(login, login.StartedAt, false);

		return writer.AppendAsync(Root.User(login.User.Id),
			new WriteCommand<AuthDbContext>(async (context, token) =>
			{
				context.Sessions.Add(row);
				await context.SaveChangesAsync(token);
			}));
	}

	/// <inheritdoc />
	public Task UpdateAsync(SessionRecord session, DateTimeOffset lastActiveAt, bool mustReauthenticate,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var row = ToRow(session, lastActiveAt, mustReauthenticate);
		return writer.SaveAsync(Root.User(session.User.Id), row.Id,
			new WriteCommand<AuthDbContext>(async (context, token) =>
			{
				await context.ReplaceAsync(row, item => item.Id == row.Id, token);
			}));
	}

	/// <inheritdoc />
	public async ValueTask<Page<SessionEntry>> ListAsync(UserRecord user, SessionFilter filter, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var types = filter.Types;
		var now = filter.Now.ToUniversalTime();
		var (total, rows) = await reader.ReadAsync(async (AuthDbContext context) =>
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

		return new Page<SessionEntry>(rows.Select(row => ToEntry(row, user)).ToList(), total);
	}

	/// <inheritdoc />
	public async Task<SessionEntry?> GetAsync(UserRecord user, Guid id, CancellationToken cancellationToken = default)
	{
		var row = await reader.ReadAsync((AuthDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.Id == id && item.UserId == user.Id, cancellationToken),
			cancellationToken);
		return row is null ? null : ToEntry(row, user);
	}

	/// <inheritdoc />
	public async Task<StoredSession?> FindByTokenAsync(SessionType type, byte[] tokenHash, DateTimeOffset now,
		CancellationToken cancellationToken = default)
	{
		var at = now.ToUniversalTime();
		var row = await reader.ReadAsync((AuthDbContext context) => context.Sessions
				.FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.Type == type && item.EndedAt == null &&
				                             (item.ExpiresAt == null || item.ExpiresAt > at), cancellationToken),
			cancellationToken);
		return row is null
			? null
			: new StoredSession(row.Id, row.UserId, row.Type, row.Ip, row.ClientInfo, row.StartedAt, row.ExpiresAt);
	}

	/// <inheritdoc />
	public Task EndAsync(UserRecord user, Guid? only, Guid? except, IReadOnlyList<SessionType> types,
		DateTimeOffset at, SessionEndReason reason, bool requireReauthentication,
		CancellationToken cancellationToken = default)
	{
		var end = at.ToUniversalTime();
		return writer.AppendAsync(Root.User(user.Id), new WriteCommand<AuthDbContext>(async (context, token) =>
		{
			var query = context.Sessions.Where(row => row.UserId == user.Id && types.Contains(row.Type));
			if (only is { } one) query = query.Where(row => row.Id == one);
			if (except is { } kept) query = query.Where(row => row.Id != kept);

			await query.Where(row => row.EndedAt == null).ExecuteUpdateAsync(setters => setters
				.SetProperty(row => row.EndedAt, end)
				.SetProperty(row => row.EndReason, reason), token);

			// A device is marked even when its session had ended already: the saved password must stop working there.
			if (requireReauthentication)
				await query.Where(row => row.Type == SessionType.Stable || row.Type == SessionType.StableTourney)
					.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.MustReauthenticate, true), token);
		}));
	}

	/// <inheritdoc />
	public Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity,
		CancellationToken cancellationToken = default)
	{
		if (activity.Count == 0) return Task.CompletedTask;

		return writer.AppendAsync(Root.Server, new WriteCommand<AuthDbContext>(async (context, token) =>
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
		}));
	}

	/// <inheritdoc />
	public Task<int> DeleteEndedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
	{
		var before = cutoff.ToUniversalTime();
		return writer.WriteAsync(Root.Server, async (AuthDbContext context, CancellationToken token) =>
			await context.Sessions.Where(row => !row.MustReauthenticate &&
			                                    ((row.EndedAt != null && row.EndedAt < before) ||
			                                     (row.EndedAt == null && row.ExpiresAt != null &&
			                                      row.ExpiresAt < before)))
				.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> TakeReauthenticationAsync(UserRecord user, DeviceId device,
		CancellationToken cancellationToken = default)
	{
		return await writer.WriteAsync(Root.User(user.Id), async (AuthDbContext context, CancellationToken token) =>
				await context.Sessions.Where(row => row.UserId == user.Id && row.MustReauthenticate &&
				                                    row.UninstallHash == device.UninstallHash.HashValue &&
				                                    row.DiskSignatureHash == device.DiskSignatureHash.HashValue)
					.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.MustReauthenticate, false), token) >
				0,
			cancellationToken);
	}

	private static SessionEntry ToEntry(SessionRow row, UserRecord user)
	{
		return new SessionEntry(row.Id, user, row.Type, row.Ip, row.ClientInfo, row.StartedAt, row.LastActiveAt,
			row.EndedAt, row.EndReason, row.MustReauthenticate);
	}

	private static SessionRow ToRow(SessionRecord session, DateTimeOffset lastActiveAt, bool mustReauthenticate)
	{
		var (device, credential, end) = session.Value;
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
			MustReauthenticate = mustReauthenticate,
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