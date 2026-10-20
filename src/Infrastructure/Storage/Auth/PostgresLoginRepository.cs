using System.Globalization;
using System.Net;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Common;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Postgres;
using Dapper;

namespace Basil.Infrastructure.Storage.Auth;

/// <summary>Records the history of logins.</summary>
internal sealed class PostgresLoginRepository(DatabaseWriter writer, DatabaseReader reader) : ILoginRepository
{
	/// <inheritdoc />
	public Task RecordAsync(Login login, CancellationToken cancellationToken = default)
	{
		var client = login.Client;
		var values = new
		{
			UserId = login.User.Id,
			login.Ip,
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

		_ = writer.AppendAsync(Root.User(login.User.Id), new WriteCommand(
			"""
			insert into logins (user_id, ip, timestamp, client_date, client_revision, client_stream, osu_path_hash,
			                    network_adapters, network_adapters_hash, uninstall_hash, disk_signature_hash)
			values (@UserId, @Ip, @Timestamp, @ClientDate, @ClientRevision, @ClientStream, @OsuPathHash,
			        @NetworkAdapters, @NetworkAdaptersHash, @UninstallHash, @DiskSignatureHash)
			""",
			values));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask<Page<LoginEntry>> ListAsync(UserRecord user, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var (total, rows) = await reader.ReadAsync(async connection => (
			await connection.ExecuteScalarAsync<int>("select count(*) from logins where user_id = @UserId",
				new { UserId = user.Id }),
			await connection.QueryAsync<LoginRow>(
				"""
				select ip, timestamp
				from logins
				where user_id = @UserId
				order by timestamp desc, id desc
				limit @Limit offset @Offset
				""",
				new { UserId = user.Id, page.Limit, page.Offset })), cancellationToken);

		var logins = rows.Select(row => new LoginEntry(user, row.Ip, row.Timestamp)).ToList();
		return new Page<LoginEntry>(logins, total);
	}

	/// <summary>A stored row of the <c>logins</c> table, holding only what a login history shows.</summary>
	private sealed class LoginRow
	{
		public IPAddress Ip { get; set; } = IPAddress.None;
		public DateTimeOffset Timestamp { get; set; }
	}
}