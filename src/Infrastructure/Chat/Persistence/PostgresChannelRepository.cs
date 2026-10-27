using System.Collections.Concurrent;
using Basil.Application.Chat.Persistence;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Chat.Persistence;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class PostgresChannelRepository(DatabaseReader reader, DatabaseWriter writer)
	: IChannelRepository, IResident
{
	private readonly ConcurrentDictionary<string, GeneralChannelRecord> _channels =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly ResidentWrites<string> _writes = new(StringComparer.OrdinalIgnoreCase);

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
	public async Task CreateOrUpdateAsync(GeneralChannelRecord channel, CancellationToken cancellationToken = default)
	{
		await _writes.WriteAsync(channel.Name,
			() => writer.SaveAsync(Root.Server, (GetType(), channel.Name.ToLowerInvariant()), Upsert(channel)),
			() => _channels[channel.Name] = channel);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(GeneralChannelRecord channel, CancellationToken cancellationToken = default)
	{
		await _writes.WriteAsync(channel.Name,
			() => writer.SaveAsync(Root.Server, (GetType(), channel.Name.ToLowerInvariant()),
				new WriteCommand<ChatDbContext>(async (context, token) =>
				{
					await context.Channels
						.Where(c => c.Name == channel.Name)
						.ExecuteDeleteAsync(token);
				})),
			() => _channels.TryRemove(channel.Name, out _));
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync((ChatDbContext context) => context.Channels
			.OrderBy(c => c.Name)
			.ToListAsync(cancellationToken), cancellationToken);
		foreach (var channel in rows.Select(ToRecord)) _channels[channel.Name] = channel;

		_loaded = true;
	}

	/// <summary>Builds the change that stores a channel, from the values it has now.</summary>
	private static WriteCommand<ChatDbContext> Upsert(GeneralChannelRecord channel)
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
		return new WriteCommand<ChatDbContext>((context, token) =>
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
				ReadPrivileges = (UserPrivileges)(byte)row.ReadPrivileges,
				WritePrivileges = (UserPrivileges)(byte)row.WritePrivileges,
				AutoJoin = row.AutoJoin,
				Visible = row.Visible
			}
		};
	}
}