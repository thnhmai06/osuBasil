using System.Collections.Concurrent;
using Basil.Application.Persistence.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Permissions;
using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Chat;

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
			async (context, token) =>
			{
				await context.Channels
					.Where(c => c.Name == channel.Name)
					.ExecuteDeleteAsync(token);
			}));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync(context => context.Channels
			.OrderBy(c => c.Name)
			.ToListAsync(cancellationToken), cancellationToken);
		foreach (var channel in rows.Select(ToRecord))
		{
			_channels[channel.Name] = channel;
		}

		_loaded = true;
	}

	/// <summary>Builds the change that stores a channel, from the values it has now.</summary>
	private static WriteCommand Upsert(GeneralChannelRecord channel)
	{
		var row = new ChannelRow
		{
			Name = channel.Name,
			Topic = channel.Value.Topic,
			ReadPrivileges = (byte)channel.Value.ReadPrivileges,
			WritePrivileges = (byte)channel.Value.WritePrivileges,
			AutoJoin = channel.Value.AutoJoin,
			Visible = channel.Value.Visible
		};
		return new WriteCommand((context, token) =>
			context.UpsertAsync(row, stored => stored.Name == row.Name, token));
	}

	/// <summary>Builds a general channel from a stored row.</summary>
	private static GeneralChannelRecord ToRecord(ChannelRow row)
	{
		return new GeneralChannelRecord
		{
			Name = row.Name,
			Value = new GeneralChannelData
			{
				Topic = row.Topic,
				ReadPrivileges = (Privileges)(byte)row.ReadPrivileges,
				WritePrivileges = (Privileges)(byte)row.WritePrivileges,
				AutoJoin = row.AutoJoin,
				Visible = row.Visible
			}
		};
	}
}