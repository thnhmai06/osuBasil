namespace Basil.Domain.Authentication;

/// <summary>Where a session stands at a moment, from how long it has been silent.</summary>
public enum SessionStatus : byte
{
	/// <summary>The session was heard from recently and is not given up.</summary>
	Active,

	/// <summary>The session has been silent for a while and a new sign-in of the same type may take its place.</summary>
	Replaceable,

	/// <summary>The session has been silent past its expiry and counts as gone.</summary>
	Expired,

	/// <summary>The session was ended.</summary>
	Ended
}