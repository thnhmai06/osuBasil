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
		/// <summary>Gets a value that indicates whether the server answers multicast DNS queries for its domain.</summary>
		public bool Enabled { get; set; }

		/// <summary>Gets how many seconds a client may keep an answer.</summary>
		public int DnsTtl { get; set; } = 120;
	}
}