using Basil.Application.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannelSession(BanchoConnection host) : ChannelSession(new SpectatorChannel(host.User))
{
	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host => host;

	/// <summary>Gets the connections spectating the host.</summary>
	public IEnumerable<Connection> Spectators => Members.Where(member => !ReferenceEquals(member, Host));
}