using Basil.Application.Sessions.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Sessions.Persistence;

internal sealed class EfCredentialRepository(DatabaseSession database, IMemoryCache memory) : ICredentialRepository
{
	// A hash of a fixed password, checked for an account that has no credential yet, so an unknown account takes
	// as long as a wrong password and cannot be told apart by timing.
	private static readonly string AbsentAccountHash = BCrypt.Net.BCrypt.HashPassword("basil-absent-account");

	// The bcrypt hash of each account that signed in, read on every login.
	private readonly CacheAside<int, string> _hashes = new(memory);

	public async Task<bool> VerifyAsync(Credentials credentials, CancellationToken ct = default)
	{
		var userId = credentials.User.Id;
		var stored = await _hashes.GetAsync(userId, () => database.ReadAsync((SessionsDbContext context) =>
			context.Credentials.Where(c => c.UserId == userId).Select(c => c.PasswordHash).FirstOrDefaultAsync(
				ct), ct));
		if (stored is null)
		{
			BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, AbsentAccountHash);
			return false;
		}

		return BCrypt.Net.BCrypt.Verify(credentials.PasswordHash.HashValue, stored);
	}

	public async Task CreateOrUpdateAsync(Credentials credentials)
	{
		var row = new CredentialRow
		{
			UserId = credentials.User.Id,
			PasswordHash = BCrypt.Net.BCrypt.HashPassword(credentials.PasswordHash.HashValue)
		};
		await database.WriteAsync((SessionsDbContext context) =>
			context.UpsertAsync(row, stored => stored.UserId == row.UserId));
		_hashes.Remove(row.UserId);
	}

	public async Task DeleteAsync(UserRecord user)
	{
		await database.WriteAsync((SessionsDbContext context) => context.Credentials
			.Where(c => c.UserId == user.Id)
			.ExecuteDeleteAsync());
		_hashes.Remove(user.Id);
	}
}