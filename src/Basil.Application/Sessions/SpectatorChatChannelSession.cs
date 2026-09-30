using Basil.Domain.Chat;
using Basil.Application.Chat;

namespace Basil.Application.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChatChannelSession(BanchoConnection host)
	: ChatChannelSession(new SpectatorChatChannel(host.User))
{
	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host => host;

	/// <inheritdoc />
	public override bool CanRead(Connection connection) => ReferenceEquals(connection, host);

	/// <inheritdoc />
	public override bool CanWrite(Connection connection) => CanRead(connection);
}