using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class PostgresChannelRepository(Database database) : IChannelRepository
{
	private ImmutableList<GeneralChannel>? _channels;

	/// <inheritdoc />
	public async Task<IReadOnlyList<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _channels) is { } cached)
			return cached;

		var rows = await database.ReadAsync(connection => connection.QueryAsync<ChannelRow>(
			"select name, topic, read_permissions, write_permissions, auto_join, visible from channels order by name"), cancellationToken);
		var loaded = rows.Select(row => row.ToChannel()).ToImmutableList();
		return Interlocked.CompareExchange(ref _channels, loaded, null) ?? loaded;
	}

	/// <summary>A stored row of the <c>channels</c> table.</summary>
	private sealed class ChannelRow
	{
		public string Name { get; set; } = "";
		public string Topic { get; set; } = "";
		public long ReadPermissions { get; set; }
		public long WritePermissions { get; set; }
		public bool AutoJoin { get; set; }
		public bool Visible { get; set; }

		/// <summary>Builds a general channel from this row.</summary>
		public GeneralChannel ToChannel()
		{
			return new GeneralChannel
			{
				Name = Name,
				Topic = Topic,
				ReadPermissions = (Permissions)(ulong)ReadPermissions,
				WritePermissions = (Permissions)(ulong)WritePermissions,
				AutoJoin = AutoJoin,
				Visible = Visible
			};
		}
	}
}
