using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class SqliteCredentialRepository(Database database) : ICredentialRepository
{
	/// <inheritdoc />
	public async Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var stored = await connection.QuerySingleOrDefaultAsync<string?>(
			"SELECT PasswordHash FROM Credentials WHERE UserId = @UserId",
			new { UserId = credentials.User.Id });

		return stored is not null && BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, stored);
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		var hash = BCrypt.Net.BCrypt.HashPassword(credentials.PasswordHash.HashValue);

		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Credentials (UserId, PasswordHash)
			VALUES (@UserId, @PasswordHash)
			ON CONFLICT(UserId) DO UPDATE SET PasswordHash = excluded.PasswordHash;
			""",
			new { UserId = credentials.User.Id, PasswordHash = hash });
	}
}
