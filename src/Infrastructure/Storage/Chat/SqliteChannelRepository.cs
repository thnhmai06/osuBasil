using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Caching;
using Dapper;

namespace Basil.Infrastructure.Storage.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class SqliteChannelRepository(Database database, WriteBuffer buffer) : IChannelRepository
{
	private ImmutableList<GeneralChannel>? _channels;

	/// <inheritdoc />
	public async Task<IReadOnlyList<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _channels) is { } cached)
			return cached;

		await buffer.FlushAsync(cancellationToken);
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<ChannelRow>(
			"SELECT Name, Topic, ReadPermissions, WritePermissions, AutoJoin, Visible FROM Channels ORDER BY Name");
		var loaded = rows.Select(row => row.ToChannel()).ToImmutableList();
		return Interlocked.CompareExchange(ref _channels, loaded, null) ?? loaded;
	}

	/// <summary>A stored row of the Channels table.</summary>
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
