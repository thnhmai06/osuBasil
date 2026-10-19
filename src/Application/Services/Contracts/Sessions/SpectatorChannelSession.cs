using Basil.Application.Services.Contracts.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannelSession(BanchoConnection host) : ChannelSession
{
	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host => host;

	/// <summary>Gets the spectator channel this session holds.</summary>
	public override SpectatorChannel Channel { get; } = new(host.User);
}
