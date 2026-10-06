using System.Globalization;
using System.Net;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Domain.Utilities;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the history of logins.</summary>
internal sealed class SqliteLoginRepository(Database database, IUserRepository users) : ILoginRepository
{
	/// <inheritdoc />
	public async Task CreateAsync(Login login, CancellationToken cancellationToken = default)
	{
		var client = login.Client;

		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Logins (UserId, Ip, Timestamp, ClientDate, ClientRevision, ClientStream, OsuPathHash,
			                    NetworkAdapters, NetworkAdaptersHash, UninstallHash, DiskSignatureHash)
			VALUES (@UserId, @Ip, @Timestamp, @ClientDate, @ClientRevision, @ClientStream, @OsuPathHash,
			        @NetworkAdapters, @NetworkAdaptersHash, @UninstallHash, @DiskSignatureHash);
			""",
			new
			{
				UserId = login.User.Id,
				Ip = login.Ip.ToString(),
				Timestamp = login.Timestamp.ToUnixTimeMilliseconds(),
				ClientDate = client?.Version.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				ClientRevision = client?.Version.Revision,
				ClientStream = client is null ? null : (long?)client.Version.Stream,
				OsuPathHash = client?.Fingerprint.OsuPathHash.HashValue,
				NetworkAdapters = client?.Fingerprint.NetworkAdapters.Adapters,
				NetworkAdaptersHash = client?.Fingerprint.NetworkAdapters.Hash.HashValue,
				UninstallHash = client?.Fingerprint.UninstallHash.HashValue,
				DiskSignatureHash = client?.Fingerprint.DiskSignatureHash.HashValue
			});
	}

	/// <inheritdoc />
	public async Task<Page<Login>> ListAsync(LoginQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		var parameters = new DynamicParameters();
		var where = "";
		if (query.User is { } filter)
		{
			where = "WHERE UserId = @UserId";
			parameters.Add("UserId", filter.Id);
		}

		await using var connection = await database.OpenAsync(cancellationToken);
		var total = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Logins {where}", parameters);

		parameters.Add("Limit", page.Limit);
		parameters.Add("Offset", page.Offset);
		var rows = await connection.QueryAsync<LoginRow>(
			$"""
			 SELECT Id, UserId, Ip, Timestamp, ClientDate, ClientRevision, ClientStream, OsuPathHash,
			        NetworkAdapters, NetworkAdaptersHash, UninstallHash, DiskSignatureHash
			 FROM Logins
			 {where}
			 ORDER BY Timestamp DESC, Id DESC LIMIT @Limit OFFSET @Offset
			 """,
			parameters);

		var items = new List<Login>(rows.Count());
		foreach (var row in rows)
		{
			var user = query.User ?? await users.GetAsync(row.UserId, cancellationToken);
			if (user is null)
				continue;

			items.Add(ToLogin(row, user));
		}

		return new Page<Login>(items, total);
	}

	/// <summary>Builds a login from a stored row.</summary>
	private static Login ToLogin(LoginRow row, User user)
	{
		return new Login
		{
			User = user,
			Ip = IPAddress.Parse(row.Ip),
			Client = row.ClientDate is { } date
				? new ClientInfo(
					new ClientVersion(
						DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
						row.ClientRevision,
						(ClientVersionStream)row.ClientStream!.Value),
					new ClientFingerprint(
						new Md5(row.OsuPathHash!),
						new NetworkAdapters(row.NetworkAdapters!, new Md5(row.NetworkAdaptersHash!)),
						new Md5(row.UninstallHash!),
						new Md5(row.DiskSignatureHash!)))
				: null,
			Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(row.Timestamp)
		};
	}

	/// <summary>A stored row of the Logins table.</summary>
	private sealed class LoginRow
	{
		public int Id { get; set; }
		public int UserId { get; set; }
		public string Ip { get; set; } = "";
		public long Timestamp { get; set; }
		public string? ClientDate { get; set; }
		public int? ClientRevision { get; set; }
		public long? ClientStream { get; set; }
		public string? OsuPathHash { get; set; }
		public string? NetworkAdapters { get; set; }
		public string? NetworkAdaptersHash { get; set; }
		public string? UninstallHash { get; set; }
		public string? DiskSignatureHash { get; set; }
	}
}
