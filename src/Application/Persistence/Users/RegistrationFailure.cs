namespace Basil.Application.Persistence.Users;

/// <summary>The reasons a registration is refused.</summary>
public enum RegistrationFailure : byte
{
	/// <summary>The name is not a valid osu! username.</summary>
	InvalidName,

	/// <summary>Another user already has the name.</summary>
	NameTaken,

	/// <summary>Account creation is locked to users who manage accounts.</summary>
	Locked
}