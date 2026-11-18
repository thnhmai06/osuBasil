using Basil.Application.Chat.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Chat.Persistence;

internal sealed class EfChannelRepository(DatabaseSession database) : IChannelRepository
{
	public async Task<IEnumerable<GeneralChannelRecord>> ListAsync(CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((ChatDbContext context) => context.Channels
			.OrderBy(row => row.Name)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		return rows.Select(ToRecord).OrderBy(channel => channel.Name, StringComparer.Ordinal).ToList();
	}

	public Task CreateOrUpdateAsync(GeneralChannelRecord channel)
	{
		var row = ToRow(channel);
		return database.WriteAsync((ChatDbContext context) =>
			context.UpsertAsync(row, stored => stored.Name == row.Name));
	}

	public Task DeleteAsync(GeneralChannelRecord channel)
	{
		return database.WriteAsync((ChatDbContext context) => context.Channels
			.Where(c => c.Name == channel.Name)
			.ExecuteDeleteAsync());
	}

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