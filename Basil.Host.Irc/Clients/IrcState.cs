namespace Basil.Host.Irc.Clients;

public sealed class IrcState(TimeProvider clock)
{
	public DateTimeOffset StartedAt { get; } = clock.GetUtcNow();

	public int Port { get; internal set; }
}