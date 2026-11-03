namespace Basil;

/// <summary>
///     Operational settings for the server host; a change applies after the server restarts. Where the HTTPS
///     transport listens and which certificate it uses are Kestrel's own <c>Kestrel</c> settings.
/// </summary>
public sealed class ServerOptions
{
	/// <summary>Gets the name the server calls itself, for example in telemetry.</summary>
	public string Name { get; set; } = "Basil";

	/// <summary>Gets the public domain clients use; the IRC server is <c>irc.</c> plus this domain.</summary>
	public string Domain { get; set; } = "localhost";

	/// <summary>Gets the IP addresses and CIDR ranges whose forwarding headers are trusted.</summary>
	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

	/// <summary>Gets the settings of the IRC host.</summary>
	public IrcSettings Irc { get; set; } = new();

	/// <summary>Gets the settings of the multicast DNS answers that let a client on the same network find the server.</summary>
	public MulticastSettings Multicast { get; set; } = new();

	/// <summary>The settings of the IRC host.</summary>
	public sealed class IrcSettings
	{
		/// <summary>Gets the TCP port of the IRC host; <c>0</c> picks a free port.</summary>
		public int Port { get; set; } = 6667;
	}

	/// <summary>The settings of the multicast DNS answers.</summary>
	public sealed class MulticastSettings
	{
		/// <summary>The standard multicast DNS group address of IPv4.</summary>
		public const string StandardIPv4 = "224.0.0.251";

		/// <summary>The standard multicast DNS group address of IPv6.</summary>
		public const string StandardIPv6 = "FF02::FB";

		/// <summary>The standard multicast DNS port.</summary>
		public const int StandardPort = 5353;

		/// <summary>Gets a value that indicates whether the server answers multicast DNS queries for its domain.</summary>
		public bool Enabled { get; set; }

		/// <summary>Gets the IPv4 group address to answer on; empty switches IPv4 off. Only the standard address is supported.</summary>
		public string? IPv4 { get; set; } = StandardIPv4;

		/// <summary>Gets the IPv6 group address to answer on; empty switches IPv6 off. Only the standard address is supported.</summary>
		public string? IPv6 { get; set; } = StandardIPv6;

		/// <summary>Gets the UDP port to answer on. Only the standard port is supported.</summary>
		public int Port { get; set; } = StandardPort;

		/// <summary>Gets how many seconds a client may keep an answer.</summary>
		public int DnsTtl { get; set; } = 120;
	}
}