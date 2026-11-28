namespace Basil.Host.Irc.Transport;

public sealed class IrcOptions
{
	public int Port { get; set; } = 6667;

	public string Domain { get; set; } = "localhost";

	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

	public string ServerName => $"irc.{Domain}";
}