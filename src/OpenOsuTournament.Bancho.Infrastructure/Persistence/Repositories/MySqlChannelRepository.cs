using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IChannelRepository" />
public sealed class MySqlChannelRepository(string connectionString) : IChannelRepository
{
    private const string SelectColumns =
        "id, name, topic, read_priv AS ReadPriv, write_priv AS WritePriv, auto_join AS AutoJoin";

    public async Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<ChannelRow>(
            $"SELECT {SelectColumns} FROM channels WHERE auto_join = @AutoJoin", new { AutoJoin = true });
        return rows.Select(r => r.ToChannel()).ToList();
    }

    public async Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<ChannelRow>(
            $"SELECT {SelectColumns} FROM channels WHERE name = @Name",
            new { Name = name });
        return row?.ToChannel();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
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
            return new Channel(Id, Name, Topic, ReadPriv, WritePriv, AutoJoin);
        }
    }
}