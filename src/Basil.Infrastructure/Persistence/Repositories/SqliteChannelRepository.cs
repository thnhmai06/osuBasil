using Basil.Application.Abstractions.Channels;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IChannelRepository" />
public sealed class SqliteChannelRepository(string connectionString) : IChannelRepository
{
    public async Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<ChannelRow>(
            "SELECT * FROM Channels WHERE AutoJoin = @AutoJoin", new { AutoJoin = true });
        return rows.Select(r => r.ToChannel()).ToList();
    }

    public async Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<ChannelRow>(
            "SELECT * FROM Channels WHERE Name = @Name",
            new { Name = name });
        return row?.ToChannel();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class ChannelRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Topic { get; set; } = "";
        public int ReadPriv { get; set; }
        public int WritePriv { get; set; }
        public bool AutoJoin { get; set; }

        public Channel ToChannel()
        {
            return new Channel(Id, Name, Topic, (UserPrivileges)ReadPriv, (UserPrivileges)WritePriv, AutoJoin);
        }
    }
}
