using Basil.Application.Services.Contracts.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChannelSession : ChannelSession
{
	internal SpectatorChannelSession(BanchoConnection host, ChannelContext context) : base(context)
	{
		Host = host;
		Channel = new SpectatorChannel(host.User);
	}

	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host { get; }

	/// <summary>Gets the spectator channel this session holds.</summary>
	public override SpectatorChannel Channel { get; }
}
