using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class PostgresCredentialRepository(Database database, DatabaseWriter writer)
	: CachedRepository<int, PostgresCredentialRepository.StoredCredential>(database, writer), ICredentialRepository
{
	protected override int KeyOf(StoredCredential item) => item.UserId;

	protected override Root RootOf(StoredCredential item) => Root.User(item.UserId);

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
		var item = Track(new StoredCredential(credentials.User.Id, hash));
		item.Hash = hash;
		_ = SaveAsync(item);
		return Task.CompletedTask;
	}

	protected override async Task<StoredCredential?> LoadAsync(int key, CancellationToken cancellationToken)
	{
		var hash = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<string?>(
			"select password_hash from credentials where user_id = @UserId", new { UserId = key }), cancellationToken);
		return hash is null ? null : new StoredCredential(key, hash);
	}

	protected override string WriteSql =>
		"""
			insert into credentials (user_id, password_hash)
			values (@UserId, @PasswordHash)
			on conflict (user_id) do update set password_hash = excluded.password_hash;
			""";

	protected override object WriteParameters(StoredCredential item) => new { item.UserId, PasswordHash = item.Hash };

	/// <summary>Holds one user's stored credential hash.</summary>
	internal sealed class StoredCredential(int userId, string hash)
	{
		public int UserId { get; } = userId;
		public string Hash { get; set; } = hash;
	}
}
