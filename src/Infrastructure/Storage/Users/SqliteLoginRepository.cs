using System.Globalization;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Records the history of logins.</summary>
internal sealed class SqliteLoginRepository(Database database) : ILoginRepository
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
}
