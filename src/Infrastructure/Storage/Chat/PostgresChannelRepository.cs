using System.Collections.Concurrent;
using Basil.Application.Storage.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class PostgresChannelRepository(DatabaseReader reader, DatabaseWriter writer)
	: IChannelRepository, IResident
{
	private readonly ConcurrentDictionary<string, GeneralChannelRecord> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	private volatile bool _loaded;

	/// <inheritdoc />
	public Task<IEnumerable<GeneralChannelRecord>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (!_loaded)
			throw new InvalidOperationException("Server channels are read before the storage has started.");
		return Task.FromResult<IEnumerable<GeneralChannelRecord>>(
			_channels.Values.OrderBy(channel => channel.Name, StringComparer.Ordinal));
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(GeneralChannelRecord channel, CancellationToken cancellationToken = default)
	{
		_channels[channel.Name] = channel;
		_ = writer.SaveAsync(Root.Server, (GetType(), channel.Name.ToLowerInvariant()), Upsert(channel));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task DeleteAsync(GeneralChannelRecord channel, CancellationToken cancellationToken = default)
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
			var channel = row.ToRecord();
			_channels[channel.Name] = channel;
		}

		_loaded = true;
	}

	private static WriteCommand Upsert(GeneralChannelRecord channel)
	{
		var readPrivileges = (int)(byte)channel.Value.ReadPrivileges;
		var writePrivileges = (int)(byte)channel.Value.WritePrivileges;
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
				channel.Value.Topic,
				ReadPrivileges = readPrivileges,
				WritePrivileges = writePrivileges,
				channel.Value.AutoJoin,
				channel.Value.Visible
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
		public GeneralChannelRecord ToRecord()
		{
			return new GeneralChannelRecord
			{
				Name = Name,
				Value = new GeneralChannelData
				{
					Topic = Topic,
					ReadPrivileges = (Privileges)(byte)ReadPrivileges,
					WritePrivileges = (Privileges)(byte)WritePrivileges,
					AutoJoin = AutoJoin,
					Visible = Visible
				}
			};
		}
	}
}