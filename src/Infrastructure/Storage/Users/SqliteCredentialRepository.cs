using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class SqliteCredentialRepository(Database database, WriteBuffer buffer)
	: CachedRepository<int, SqliteCredentialRepository.StoredCredential>(database, buffer), ICredentialRepository
{
	protected override int KeyOf(StoredCredential item) => item.UserId;

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
		Save(item);
		return Task.CompletedTask;
	}

	protected override async Task<StoredCredential?> ReadAsync(SqliteConnection connection, int key,
		CancellationToken cancellationToken)
	{
		var hash = await connection.QuerySingleOrDefaultAsync<string?>(
			"SELECT PasswordHash FROM Credentials WHERE UserId = @UserId", new { UserId = key });
		return hash is null ? null : new StoredCredential(key, hash);
	}

	protected override Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, StoredCredential item)
	{
		var hash = item.Hash;
		return connection.ExecuteAsync(
			"""
			INSERT INTO Credentials (UserId, PasswordHash)
			VALUES (@UserId, @PasswordHash)
			ON CONFLICT(UserId) DO UPDATE SET PasswordHash = excluded.PasswordHash;
			""",
			new { item.UserId, PasswordHash = hash }, transaction);
	}

	/// <summary>Holds one user's stored credential hash.</summary>
	internal sealed class StoredCredential(int userId, string hash)
	{
		public int UserId { get; } = userId;
		public string Hash { get; set; } = hash;
	}
}
