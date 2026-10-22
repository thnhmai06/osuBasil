using Basil.Application.Persistence.Auth;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Memory;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Auth;

/// <summary>Stores the login credential of each user.</summary>
internal sealed class PostgresCredentialRepository(DatabaseReader reader, DatabaseWriter writer)
	: MemoryRepository<int, PostgresCredentialRepository.StoredCredential>(reader, writer), ICredentialRepository
{
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

	/// <inheritdoc />
	public async Task DeleteAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(user.Id, cancellationToken) is { } stored)
			await RemoveAsync(stored);
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
		var row = await Reader.ReadAsync(context => context.Credentials
			.Where(c => c.UserId == key)
			.FirstOrDefaultAsync());
		return row is null ? null : new StoredCredential(key, row.PasswordHash);
	}

	protected override WriteCommand EraseCommand(int key)
	{
		return new WriteCommand((context, token) => context.Credentials
			.Where(c => c.UserId == key)
			.ExecuteDeleteAsync(token));
	}

	protected override WriteCommand SaveCommand(StoredCredential item)
	{
		var row = new CredentialRow { UserId = item.UserId, PasswordHash = item.Hash };
		return new WriteCommand((context, token) => context.UpsertAsync(row, stored => stored.UserId == row.UserId, token));
	}

	/// <summary>Holds one user's stored credential hash.</summary>
	internal sealed class StoredCredential(int userId, string hash)
	{
		public int UserId { get; } = userId;
		public string Hash { get; set; } = hash;
	}
}