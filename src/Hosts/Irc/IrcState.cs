namespace Basil.Host.Irc;

/// <summary>Holds whether the IRC side of the server takes new connections.</summary>
public sealed class IrcState(TimeProvider clock)
{
	/// <summary>Gets or sets whether new connections are accepted; the server turns it off before it shuts down.</summary>
	public volatile bool AcceptingLogins = true;

	/// <summary>Gets when the IRC side of the server started.</summary>
	public DateTimeOffset StartedAt { get; } = clock.GetUtcNow();

	/// <summary>Gets the TCP port the IRC listener is bound to; <c>0</c> until it started.</summary>
	public int Port { get; internal set; }
}