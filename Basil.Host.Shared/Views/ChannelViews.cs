using Basil.Domain.Chat;

namespace Basil.Host.Shared.Views;

public sealed record ChannelView(
	string Name,
	string Topic,
	byte ReadPrivileges,
	byte WritePrivileges,
	bool AutoJoin,
	bool Visible)
{
	public static ChannelView Of(GeneralChannelRecord record)
	{
		var value = record.Value;
		return new ChannelView(record.Name, value.Topic, (byte)value.ReadPrivileges, (byte)value.WritePrivileges,
			value.AutoJoin, value.Visible);
	}
}