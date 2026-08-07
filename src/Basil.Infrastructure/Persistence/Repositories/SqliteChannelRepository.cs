using Basil.Application.Abstractions.Channels;
using Basil.Domain.Channels;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IChannelRepository" />
/// <remarks>
///     Rows map through the private mutable <c>ChannelRow</c> DTO. Each method opens its own
///     connection.
/// </remarks>
public sealed class SqliteChannelRepository(string connectionString) : IChannelRepository
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<Channel>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<ChannelRow>("SELECT * FROM Channels");
		return [.. rows.Select(r => r.ToChannel())];
	}

	/// <inheritdoc />
	public async Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<ChannelRow>(
			"SELECT * FROM Channels WHERE Name = @Name",
			new { Name = name });
		return row?.ToChannel();
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the Channels table columns.
	/// </summary>
	private sealed class ChannelRow
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
		public string Topic { get; set; } = "";
		public int ReadPrivilege { get; set; }
		public int WritePrivilege { get; set; }
		public bool AutoJoin { get; set; }

		/// <summary>Builds a <see cref="Channel" /> from this row, casting the stored privilege columns.</summary>
		/// <returns>The domain channel.</returns>
		public Channel ToChannel()
		{
			return new Channel(
				Id, Name, Topic, (UserPrivileges)ReadPrivilege, (UserPrivileges)WritePrivilege, AutoJoin);
		}
	}
}