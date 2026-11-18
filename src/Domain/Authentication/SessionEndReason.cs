namespace Basil.Domain.Authentication;

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