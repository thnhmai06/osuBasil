namespace Basil.Host;

/// <summary>Operational settings for the server host.</summary>
public sealed class ServerOptions
{
	/// <summary>Gets the public domain clients use; the IRC server is <c>irc.</c> plus this domain.</summary>
	public string Domain { get; set; } = "localhost";

	/// <summary>Gets the ports the transports listen on besides the HTTP ones.</summary>
	public PortOptions Ports { get; set; } = new();

	/// <summary>Gets the IP addresses and CIDR ranges whose forwarding headers are trusted.</summary>
	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

	/// <summary>The ports of the transports that are not HTTP.</summary>
	public sealed class PortOptions
	{
		/// <summary>Gets the TCP port of the IRC host; <c>0</c> picks a free port.</summary>
		public int Irc { get; set; } = 6667;
	}
}
