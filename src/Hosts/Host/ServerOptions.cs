namespace Basil.Host;

/// <summary>Operational settings for the server host.</summary>
public sealed class ServerOptions
{
	/// <summary>Gets the IP addresses and CIDR ranges whose forwarding headers are trusted.</summary>
	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];
}
