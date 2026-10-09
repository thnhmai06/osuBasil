using System.Globalization;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Infrastructure.Storage.Writing;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Records the history of logins.</summary>
internal sealed class PostgresLoginRepository(Database database, DatabaseWriter writer) : ILoginRepository
{
	private readonly IdSequence _ids = new(database, "logins");

	/// <inheritdoc />
	public async Task CreateAsync(Login login, CancellationToken cancellationToken = default)
	{
		var client = login.Client;
		var id = await _ids.NextAsync(cancellationToken);
		var values = new
		{
			Id = id,
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
			insert into logins (id, user_id, ip, timestamp, client_date, client_revision, client_stream, osu_path_hash,
			                    network_adapters, network_adapters_hash, uninstall_hash, disk_signature_hash)
			values (@Id, @UserId, @Ip, @Timestamp, @ClientDate, @ClientRevision, @ClientStream, @OsuPathHash,
			        @NetworkAdapters, @NetworkAdaptersHash, @UninstallHash, @DiskSignatureHash)
			on conflict (id) do nothing;
			""",
			values));
	}
}