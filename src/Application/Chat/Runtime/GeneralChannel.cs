using Basil.Application.Users.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Runtime;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannel : Channel
{
	internal GeneralChannel(GeneralChannelRecord record, ChannelContext context) : base(context)
	{
		Record = record;
	}

	/// <summary>Gets the stored identity and settings of this channel.</summary>
	public GeneralChannelRecord Record { get; }

	/// <inheritdoc />
	public override string Name => Record.Name;

	/// <inheritdoc />
	public override string Topic => Record.Value.Topic;

	/// <inheritdoc />
	internal override bool CanRead(Connection connection, DateTimeOffset now)
	{
		return connection.User.EffectivePermissions(now).Privileges.Allows(Record.Value.ReadPrivileges);
	}

	/// <inheritdoc />
	internal override bool CanWrite(Connection connection, DateTimeOffset now)
	{
		return connection.User.EffectivePermissions(now).Privileges.Allows(Record.Value.WritePrivileges);
	}
}