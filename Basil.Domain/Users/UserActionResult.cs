namespace Basil.Domain.Users;

/// <summary>The outcome of an action on a user account.</summary>
public enum UserActionResult : byte
{
	/// <summary>The caller lacks the privileges the action needs.</summary>
	Denied = 0,

	/// <summary>The action was carried out.</summary>
	Ok = 1,

	/// <summary>The target's granted privileges are not a strict subset of the caller's, so the caller may not act on that user.</summary>
	NotOutranking,

	/// <summary>The target is BasilBot, which cannot be the object of the action.</summary>
	SystemAccount,

	/// <summary>The caller aimed the action at themselves and the action cannot be aimed at oneself.</summary>
	Self,

	/// <summary>The target account is deleted.</summary>
	Deleted,

	/// <summary>The password the caller supplied does not match the account's current password.</summary>
	WrongPassword,

	/// <summary>Another live account already holds the requested name.</summary>
	NameTaken,

	/// <summary>No account matches the target.</summary>
	NotFound,

	/// <summary>The area the action belongs to is locked and the caller is not its manager.</summary>
	Locked,

	/// <summary>The action may only be aimed at the caller's own account.</summary>
	NotSelf
}