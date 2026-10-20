namespace Basil.Application.Objects.Users;

/// <summary>The outcome of an account operation.</summary>
public enum UserResult
{
	/// <summary>The operation succeeded.</summary>
	Ok,

	/// <summary>The caller lacks the required permission or does not outrank the target.</summary>
	NotAuthorized,

	/// <summary>The caller has the permission, but an active restriction suspends it.</summary>
	Restricted,

	/// <summary>The target is BasilBot, whose account record is fixed.</summary>
	Protected,

	/// <summary>The restriction has no permissions to suspend or ends in the past.</summary>
	InvalidRestriction,

	/// <summary>A user cannot have a relationship with themself.</summary>
	Self,

	/// <summary>The target account is deleted.</summary>
	Deleted,

	/// <summary>The current password given is wrong.</summary>
	WrongPassword
}
