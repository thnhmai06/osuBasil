using Basil.Application.Storage.Contracts.Auth;
using Basil.Domain.Auth;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Basil.Infrastructure.Storage.Memory;
using Dapper;

namespace Basil.Infrastructure.Storage.Auth;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class PostgresCredentialRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<int, PostgresCredentialRepository.StoredCredential>(reader, writer), ICredentialRepository
{
	protected override string WriteSql =>
		"""
		insert into credentials (user_id, password_hash)
		values (@UserId, @PasswordHash)
		on conflict (user_id) do update set password_hash = excluded.password_hash;
		""";

	/// <inheritdoc />
	public async Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		var stored = await FindAsync(credentials.User.Id, cancellationToken);
		return stored is not null && BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, stored.Hash);
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		var hash = BCrypt.Net.BCrypt.HashPassword(credentials.PasswordHash.HashValue);
		_ = SaveAsync(new StoredCredential(credentials.User.Id, hash));
		return Task.CompletedTask;
	}

	protected override int KeyOf(StoredCredential item)
	{
		return item.UserId;
	}

	protected override Root RootOf(StoredCredential item)
	{
		return Root.User(item.UserId);
	}

	protected override void CopyTo(StoredCredential live, StoredCredential from)
	{
		live.Hash = from.Hash;
	}

	protected override async Task<StoredCredential?> LoadAsync(int key)
	{
		var hash = await Reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<string?>(
			"select password_hash from credentials where user_id = @UserId", new { UserId = key }));
		return hash is null ? null : new StoredCredential(key, hash);
	}

	protected override object WriteParameters(StoredCredential item)
	{
		return new { item.UserId, PasswordHash = item.Hash };
	}

	/// <summary>Holds one user's stored credential hash.</summary>
	internal sealed class StoredCredential(int userId, string hash)
	{
		public int UserId { get; } = userId;
		public string Hash { get; set; } = hash;
	}
}