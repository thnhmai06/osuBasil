namespace Basil.Host;

/// <summary>Operational settings for the server host; a change applies after the server restarts.</summary>
public sealed class ServerOptions
{
	/// <summary>Gets the public domain clients use; the IRC server is <c>irc.</c> plus this domain.</summary>
	public string Domain { get; set; } = "localhost";

	/// <summary>Gets the address the server listens on.</summary>
	public string Bind { get; set; } = "0.0.0.0";

	/// <summary>Gets the ports the transports listen on.</summary>
	public PortOptions Ports { get; set; } = new();

	/// <summary>Gets the TLS certificate of the HTTPS port; the port serves plain HTTP when there is none.</summary>
	public CertificateOptions Certificate { get; set; } = new();

	/// <summary>Gets a value that indicates whether the server answers multicast DNS queries for its domain.</summary>
	public bool LanAdvertise { get; set; } = true;

	/// <summary>Gets the IP addresses and CIDR ranges whose forwarding headers are trusted.</summary>
	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

	/// <summary>The ports of the transports.</summary>
	public sealed class PortOptions
	{
		/// <summary>Gets the HTTPS port, which serves osu! and the HTTP API; <c>0</c> picks a free port.</summary>
		public int Https { get; set; } = 443;

		/// <summary>Gets the TCP port of the IRC host; <c>0</c> picks a free port.</summary>
		public int Irc { get; set; } = 6667;
	}

	/// <summary>Where the TLS certificate is.</summary>
	public sealed class CertificateOptions
	{
		/// <summary>Gets the path of the PFX file, relative to the executable; empty serves plain HTTP.</summary>
		public string? Path { get; set; }

		/// <summary>Gets the password of the PFX file.</summary>
		public string? Password { get; set; }
	}
}
