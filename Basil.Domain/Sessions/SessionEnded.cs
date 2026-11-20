namespace Basil.Domain.Sessions;

/// <summary>How and when a session ended.</summary>
/// <param name="At">The moment the session ended.</param>
/// <param name="Reason">Why the session ended.</param>
public record struct SessionEnded(DateTimeOffset At, SessionEndReason Reason);

/// <summary>Why a session ended.</summary>
public enum SessionEndReason : byte
{
	/// <summary>The user signed out.</summary>
	LoggedOut,

	/// <summary>The connection of the session dropped.</summary>
	ConnectionLost,

	/// <summary>The session was silent past its expiry.</summary>
	TimedOut,

	/// <summary>A new sign-in of the same type took its place while it was silent.</summary>
	Replaced,

	/// <summary>The account was deleted.</summary>
	Deleted,

	/// <summary>The account's password was changed.</summary>
	CredentialsChanged,

	/// <summary>The session was closed on purpose by someone other than its own sign-out.</summary>
	Revoked,

	/// <summary>The server stopped.</summary>
	ServerStopped
}