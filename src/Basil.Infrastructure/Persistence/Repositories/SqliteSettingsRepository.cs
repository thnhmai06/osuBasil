using Basil.Application.Abstractions.Settings;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ISettingsRepository" />
/// <remarks>
///     Every key this repository serves is seeded by the base migration, so <see cref="SetAsync" />
///     is a plain update against an existing row rather than an upsert.
/// </remarks>
public sealed class SqliteSettingsRepository(string connectionString) : ISettingsRepository
{
	/// <inheritdoc />
	public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
	{
		await using var connection = new SqliteConnection(connectionString);
		return await connection.QueryFirstOrDefaultAsync<string?>(
			"SELECT Value FROM Settings WHERE Key = @Key", new { Key = key });
	}

	/// <inheritdoc />
	public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
	{
		await using var connection = new SqliteConnection(connectionString);
		await connection.ExecuteAsync(
			"UPDATE Settings SET Value = @Value WHERE Key = @Key", new { Key = key, Value = value });
	}
}