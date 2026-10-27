using System.Globalization;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Auth.Persistence;

/// <summary>Records the history of logins.</summary>
internal sealed class PostgresLoginRepository(DatabaseWriter writer, DatabaseReader reader) : ILoginRepository
{
	/// <inheritdoc />
	public Task RecordAsync(Login login, CancellationToken cancellationToken = default)
	{
		var client = login.Client;
		// The row is the copy of the login as it is now, whatever happens to the login after.
		var row = new LoginRow
		{
			UserId = login.User.Id,
			Ip = login.Ip,
			Timestamp = login.Timestamp.ToUniversalTime(),
			ClientDate = client?.Version.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			ClientRevision = client?.Version.Revision,
			ClientStream = client is null ? null : (int?)client.Version.Stream,
			OsuPathHash = client?.Fingerprint.OsuPathHash.HashValue,
			NetworkAdapters = client?.Fingerprint.NetworkAdapters.Adapters,
			NetworkAdaptersHash = client?.Fingerprint.NetworkAdapters.Hash.HashValue,
			UninstallHash = client?.Fingerprint.UninstallHash.HashValue,
			DiskSignatureHash = client?.Fingerprint.DiskSignatureHash.HashValue
		};

		_ = writer.AppendAsync(Root.User(login.User.Id),
			new WriteCommand<AuthDbContext>(async (context, token) =>
			{
				context.Logins.Add(row);
				await context.SaveChangesAsync(token);
			}));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask<Page<LoginEntry>> ListAsync(UserRecord user, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await reader.ReadAsync(async (AuthDbContext context) =>
		{
			var query = context.Logins.Where(row => row.UserId == user.Id);
			var count = await query.CountAsync(cancellationToken);
			var rows = await query
				.OrderByDescending(row => row.Timestamp)
				.ThenByDescending(row => row.Id)
				.Skip(page.Offset)
				.Take(page.Limit)
				.ToListAsync(cancellationToken);
			return (count, rows);
		}, cancellationToken);

		var logins = rows.Select(row => new LoginEntry(user, row.Ip, row.Timestamp)).ToList();
		return new Page<LoginEntry>(logins, total);
	}
}