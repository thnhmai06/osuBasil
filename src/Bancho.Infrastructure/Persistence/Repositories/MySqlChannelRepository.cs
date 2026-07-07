using Bancho.Application.Abstractions;
using Dapper;
using MySqlConnector;
using Bancho.Application.Abstractions.Channels;

namespace Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IChannelRepository" />
public sealed class MySqlChannelRepository(string connectionString) : IChannelRepository
{
    private sealed class ChannelRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Topic { get; set; } = "";
        public int ReadPriv { get; set; }
        public int WritePriv { get; set; }
        public bool AutoJoin { get; set; }

        public Channel ToChannel() => new(Id, Name, Topic, ReadPriv, WritePriv, AutoJoin);
    }

    private const string SelectColumns = "id, name, topic, read_priv AS ReadPriv, write_priv AS WritePriv, auto_join AS AutoJoin";

    private MySqlConnection Connect() => new(connectionString);

    public async Task<IReadOnlyList<Channel>> FetchAllAutoJoinAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<ChannelRow>($"SELECT {SelectColumns} FROM channels WHERE auto_join = 1");
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
}
