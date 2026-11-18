namespace Basil.Host.Irc.Session;

public sealed class IrcState(TimeProvider clock)
{
	public volatile bool AcceptingLogins = true;

	public DateTimeOffset StartedAt { get; } = clock.GetUtcNow();

	public int Port { get; internal set; }
}