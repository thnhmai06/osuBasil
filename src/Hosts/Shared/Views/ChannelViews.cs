using Basil.Domain.Chat;

namespace Basil.Host.Shared.Views;

/// <summary>A general channel as the API shows it; the privileges are bitmasks.</summary>
public sealed record ChannelView(
	string Name,
	string Topic,
	byte ReadPrivileges,
	byte WritePrivileges,
	bool AutoJoin,
	bool Visible)
{
	/// <summary>Describes a general channel.</summary>
	public static ChannelView Of(GeneralChannelRecord record)
	{
		var value = record.Value;
		return new ChannelView(record.Name, value.Topic, (byte)value.ReadPrivileges, (byte)value.WritePrivileges,
			value.AutoJoin, value.Visible);
	}
}