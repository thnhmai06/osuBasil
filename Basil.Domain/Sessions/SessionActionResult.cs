namespace Basil.Domain.Sessions;

/// <summary>The outcome of an action on a session.</summary>
public enum SessionActionResult : byte
{
	/// <summary>The caller lacks the privileges the action needs.</summary>
	Denied = 0,

	/// <summary>The action was carried out.</summary>
	Ok = 1,

	/// <summary>The session had already ended.</summary>
	Ended,

	/// <summary>The session is still being heard from, so it does not give way.</summary>
	Active,

	/// <summary>The session had been silent past its expiry and was ended as timed out.</summary>
	Expired
}
