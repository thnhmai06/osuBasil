namespace Basil.Host.Irc;

/// <summary>Operational settings of the IRC host.</summary>
public sealed class IrcOptions
{
	/// <summary>Gets or sets the TCP port the IRC host listens on; <c>0</c> picks a free port.</summary>
	public int Port { get; set; } = 6667;

	/// <summary>Gets or sets the public domain; the server is <c>irc.</c> plus this domain and every user's host is this domain.</summary>
	public string Domain { get; set; } = "localhost";

	/// <summary>Gets the name the server calls itself in prefixes and numerics.</summary>
	public string ServerName => $"irc.{Domain}";
}
