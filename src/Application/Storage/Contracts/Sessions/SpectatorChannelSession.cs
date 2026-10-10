using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Storage.Contracts.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannelSession(BanchoConnection host) : ChannelSession
{
	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host => host;

	public override SpectatorChannel Channel { get; } = new(host.User);

	/// <summary>Gets the connections spectating the host.</summary>
	public IEnumerable<Connection> Spectators => Members.Where(member => !ReferenceEquals(member, Host));
}