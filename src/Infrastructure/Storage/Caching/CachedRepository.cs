using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Base for repositories whose stored items stay live in memory once read.</summary>
internal abstract class CachedRepository<TKey, T>(Database database, WriteBuffer buffer)
	where TKey : notnull where T : class
{
	protected IdentityMap<TKey, T> Items { get; } = new();

	protected abstract TKey KeyOf(T item);
	protected abstract Task<T?> ReadAsync(SqliteConnection connection, TKey key, CancellationToken cancellationToken);
	protected abstract Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, T item);
	protected virtual Task EraseAsync(SqliteConnection connection, SqliteTransaction transaction, TKey key)
		=> throw new NotSupportedException();

	protected ValueTask<T?> FindAsync(TKey key, CancellationToken cancellationToken)
	{
		return Items.GetOrAddAsync(key, async miss =>
		{
			await using var connection = await OpenAsync(cancellationToken);
			return await ReadAsync(connection, miss, cancellationToken);
		});
	}

	protected T Track(T item) => Items.GetOrAdd(KeyOf(item), item);

	protected void Save(T item)
	{
		var live = Track(item);
		buffer.Enqueue((GetType(), KeyOf(live)), (connection, transaction) => WriteAsync(connection, transaction, live));
	}

	protected void Remove(T item)
	{
		var key = KeyOf(item);
		Items.Remove(key);
		buffer.Enqueue((GetType(), key), (connection, transaction) => EraseAsync(connection, transaction, key));
	}

	protected async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
	{
		await buffer.WaitForWritesAsync(cancellationToken);
		return await database.OpenAsync(cancellationToken);
	}
}
