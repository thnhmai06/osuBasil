namespace Basil;

public sealed class ServerOptions
{
	public string Name { get; set; } = "Basil";

	public string Domain { get; set; } = "localhost";

	public string[] TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

	public IrcSettings Irc { get; set; } = new();

	public sealed class IrcSettings
	{
		public int Port { get; set; } = 6667;
	}
}