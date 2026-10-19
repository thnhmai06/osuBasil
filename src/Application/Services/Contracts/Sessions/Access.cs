namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Whether a user may do something, given what they are granted and what their restrictions suspend.</summary>
public enum Access : byte
{
	/// <summary>Every required permission is in effect.</summary>
	Allowed,

	/// <summary>A required permission is not granted.</summary>
	NotGranted,

	/// <summary>Every required permission is granted, but a restriction suspends one of them.</summary>
	Suspended
}