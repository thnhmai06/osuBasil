using Basil.Protocol.Bancho.Wire.Packets;
using Basil.Application.Users.Users;

namespace Basil.Host.Bancho.Support;

/// <summary>Holds whether the osu! side of the server takes new logins and tells clients about a restart.</summary>
public sealed class BanchoState
{
	/// <summary>Gets or sets whether new logins are accepted; the server turns it off before it shuts down.</summary>
	public volatile bool AcceptingLogins = true;
}

/// <summary>Announces a restart to every online osu! client.</summary>
internal sealed class RestartAnnouncer(UserRegistry registry, Outbox outbox)
{
	/// <summary>Queues the server-restart packet for every open osu! client.</summary>
	/// <param name="delay">How long the clients wait before they reconnect.</param>
	public void Announce(TimeSpan delay)
	{
		outbox.Send(registry.OpenBanchoClients(), writer => writer.RestartServer((int)delay.TotalMilliseconds));
	}
}
