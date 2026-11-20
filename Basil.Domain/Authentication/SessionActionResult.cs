namespace Basil.Domain.Authentication;

public enum SessionActionResult : byte
{
	Denied = 0,

	Ok = 1,

	/// <summary>The session had already ended.</summary>
	Ended,

	/// <summary>The session is still being heard from, so it does not give way.</summary>
	Active,

	/// <summary>The session had been silent past its expiry and was ended as timed out.</summary>
	Expired
}
