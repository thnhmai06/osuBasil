namespace Basil.Domain.Authentication;

public record struct SessionEnded(DateTimeOffset At, SessionEndReason Reason);

public enum SessionEndReason : byte
{
	LoggedOut,

	ConnectionLost,

	TimedOut,

	Replaced,

	Deleted,

	CredentialsChanged,

	Revoked,

	ServerStopped
}