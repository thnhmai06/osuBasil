using Basil.Application.Chat.Persistence;
using Basil.Application.Common;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Chat.Persistence;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class EfChannelRepository(DatabaseSession database) : IChannelRepository
{
	/// <inheritdoc />
	public async Task<IEnumerable<GeneralChannelRecord>> ListAsync(CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((ChatDbContext context) => context.Channels
			.OrderBy(row => row.Name)
			.Take(ListLimits.Ceiling)
			.ToListAsync(cancellationToken), cancellationToken);
		return rows.Select(ToRecord).OrderBy(channel => channel.Name, StringComparer.Ordinal).ToList();
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(GeneralChannelRecord channel)
	{
		var row = ToRow(channel);
		return database.WriteAsync((ChatDbContext context) =>
			context.UpsertAsync(row, stored => stored.Name == row.Name));
	}

	/// <inheritdoc />
	public Task DeleteAsync(GeneralChannelRecord channel)
	{
		return database.WriteAsync((ChatDbContext context) => context.Channels
			.Where(c => c.Name == channel.Name)
			.ExecuteDeleteAsync());
	}

	/// <summary>Builds the row that stores a channel, from the values it has now.</summary>
	private static ChannelRow ToRow(GeneralChannelRecord channel)
	{
		return new ChannelRow
		{
			Name = channel.Name,
			Topic = channel.Value.Topic,
			ReadPrivileges = (byte)channel.Value.ReadPrivileges,
			WritePrivileges = (byte)channel.Value.WritePrivileges,
			AutoJoin = channel.Value.AutoJoin,
			Visible = channel.Value.Visible
		};
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