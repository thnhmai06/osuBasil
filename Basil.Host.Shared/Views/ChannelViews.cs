using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Host.Shared.Views;

public sealed record ChannelView(
	string Name,
	string Topic,
	UserPrivileges ReadPrivileges,
	UserPrivileges WritePrivileges,
	bool AutoJoin,
	bool Visible)
{
	public static ChannelView Of(GeneralChannelRecord record)
	{
		var value = record.Value;
		return new ChannelView(record.Name, value.Topic, value.ReadPrivileges, value.WritePrivileges,
			value.AutoJoin, value.Visible);
	}
}