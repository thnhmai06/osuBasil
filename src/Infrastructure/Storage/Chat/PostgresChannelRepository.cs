using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class PostgresChannelRepository(DatabaseReader reader, DatabaseWriter writer)
	: IChannelRepository, IResident
{
	private readonly ConcurrentDictionary<string, GeneralChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
	private volatile bool _loaded;

	/// <inheritdoc />
	public Task<IEnumerable<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (!_loaded)
			throw new InvalidOperationException("Server channels are read before the storage has started.");
		return Task.FromResult<IEnumerable<GeneralChannel>>(
			_channels.Values.OrderBy(channel => channel.Name, StringComparer.Ordinal));
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(GeneralChannel channel, CancellationToken cancellationToken = default)
	{
		_channels[channel.Name] = channel;
		_ = writer.SaveAsync(Root.Server, (GetType(), channel.Name.ToLowerInvariant()), Upsert(channel));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(GeneralChannel channel, CancellationToken cancellationToken = default)
	{
		_channels.TryRemove(channel.Name, out _);
		_ = writer.SaveAsync(Root.Server, (GetType(), channel.Name.ToLowerInvariant()), new WriteCommand(
			"delete from channels where name = @Name", new { channel.Name }));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync(connection => connection.QueryAsync<ChannelRow>(
				"select name, topic, read_privileges, write_privileges, auto_join, visible from channels order by name"),
			cancellationToken);
		foreach (var row in rows)
		{
			var channel = row.ToChannel();
			_channels[channel.Name] = channel;
		}

		_loaded = true;
	}

	private static WriteCommand Upsert(GeneralChannel channel)
	{
		var readPrivileges = (int)(byte)channel.ReadPrivileges;
		var writePrivileges = (int)(byte)channel.WritePrivileges;
		return new WriteCommand(
			"""
			insert into channels (name, topic, read_privileges, write_privileges, auto_join, visible)
			values (@Name, @Topic, @ReadPrivileges, @WritePrivileges, @AutoJoin, @Visible)
			on conflict (name) do update set
				topic = excluded.topic,
				read_privileges = excluded.read_privileges,
				write_privileges = excluded.write_privileges,
				auto_join = excluded.auto_join,
				visible = excluded.visible;
			""",
			new
			{
				channel.Name,
				channel.Topic,
				ReadPrivileges = readPrivileges,
				WritePrivileges = writePrivileges,
				channel.AutoJoin,
				channel.Visible
			});
	}

	/// <summary>A stored row of the <c>channels</c> table.</summary>
	private sealed class ChannelRow
	{
		public string Name { get; set; } = "";
		public string Topic { get; set; } = "";
		public int ReadPrivileges { get; set; }
		public int WritePrivileges { get; set; }
		public bool AutoJoin { get; set; }
		public bool Visible { get; set; }

		/// <summary>Builds a general channel from this row.</summary>
		public GeneralChannel ToChannel()
		{
			return new GeneralChannel
			{
				Name = Name,
				Topic = Topic,
				ReadPrivileges = (Privileges)(byte)ReadPrivileges,
				WritePrivileges = (Privileges)(byte)WritePrivileges,
				AutoJoin = AutoJoin,
				Visible = Visible
			};
		}
	}
}