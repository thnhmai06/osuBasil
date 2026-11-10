using Basil.Application.Authentication.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Authentication.Persistence;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class EfCredentialRepository(DatabaseSession database) : ICredentialRepository
{
	// A hash of a fixed password, checked for an account that has no credential yet, so an unknown account takes
	// as long as a wrong password and cannot be told apart by timing.
	private static readonly string AbsentAccountHash = BCrypt.Net.BCrypt.HashPassword("basil-absent-account");

	// The bcrypt hash of each account that signed in, read on every login.
	private readonly CacheAside<int, string> _hashes = new();

	/// <inheritdoc />
	public async Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default)
	{
		var userId = credentials.User.Id;
		var stored = await _hashes.GetAsync(userId, () => database.ReadAsync((AuthenticationDbContext context) =>
			context.Credentials.Where(c => c.UserId == userId).Select(c => c.PasswordHash).FirstOrDefaultAsync(
				cancellationToken), cancellationToken));
		if (stored is null)
		{
			BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, AbsentAccountHash);
			return false;
		}

		return BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, stored);
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Credentials credentials)
	{
		var row = new CredentialRow
		{
			UserId = credentials.User.Id,
			PasswordHash = BCrypt.Net.BCrypt.HashPassword(credentials.PasswordHash.HashValue)
		};
		await database.WriteAsync((AuthenticationDbContext context) =>
			context.UpsertAsync(row, stored => stored.UserId == row.UserId));
		_hashes.Remove(row.UserId);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(UserRecord user)
	{
		await database.WriteAsync((AuthenticationDbContext context) => context.Credentials
			.Where(c => c.UserId == user.Id)
			.ExecuteDeleteAsync());
		_hashes.Remove(user.Id);
	}
}