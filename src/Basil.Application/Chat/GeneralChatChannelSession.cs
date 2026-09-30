using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>A configured chat channel while it is open.</summary>
public sealed class GeneralChatChannelSession(GeneralChatChannel channel) : ChatChannelSession(channel)
{
	/// <summary>Gets the configured channel this session runs.</summary>
	public new GeneralChatChannel Channel => channel;

	/// <inheritdoc />
	public override bool CanRead(Connection connection) => connection.User.Value.Privilege.Has(channel.ReadPrivilege);

	/// <inheritdoc />
	public override bool CanWrite(Connection connection) => connection.User.Value.Privilege.Has(channel.WritePrivilege);
}