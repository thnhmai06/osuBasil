using Basil.Application.Sessions;
using Basil.Domain.Chat;
using Basil.Domain.Client;

namespace Basil.Application.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChannelSession(GeneralChannel channel, TimeProvider time)
	: ChannelSession(channel, time)
{
	/// <summary>Gets the configured channel this session runs.</summary>
	public new GeneralChannel Channel => channel;

	/// <inheritdoc />
	public override bool CanRead(Connection connection)
	{
		return connection.User.Value.Privilege.Has(channel.ReadPrivilege);
	}

	/// <inheritdoc />
	public override bool CanWrite(Connection connection)
	{
		return connection.User.Value.Privilege.Has(channel.WritePrivilege);
	}
}