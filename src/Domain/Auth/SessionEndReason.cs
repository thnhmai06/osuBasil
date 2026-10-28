namespace Basil.Domain.Auth;

/// <summary>Why a client session stopped being active.</summary>
public enum SessionEndReason : byte
{
	/// <summary>The client logged out or quit.</summary>
	LoggedOut,

	/// <summary>The transport connection was lost.</summary>
	ConnectionLost,

	/// <summary>The client sent nothing for too long.</summary>
	TimedOut,

	/// <summary>A new session of the same kind replaced this idle session.</summary>
	Replaced,

	/// <summary>The user's account was deleted.</summary>
	Deleted,

	/// <summary>The user's password was changed.</summary>
	CredentialsChanged,

	/// <summary>The user's sessions were ended, or the user may no longer use this kind of client.</summary>
	Revoked
}
