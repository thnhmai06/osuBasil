namespace Basil.Application.Runtime.Users;

/// <summary>What happened to an account operation that was not refused for lack of permissions.</summary>
public enum UserOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1,

	/// <summary>The caller does not outrank the target account.</summary>
	NotOutranking,

	/// <summary>The target is BasilBot, whose account record is fixed.</summary>
	Protected,

	/// <summary>The restriction has no permissions to suspend or ends in the past.</summary>
	InvalidRestriction,

	/// <summary>A user cannot have a relationship with themself.</summary>
	Self,

	/// <summary>The target account is deleted.</summary>
	Deleted,

	/// <summary>The current password given is wrong.</summary>
	WrongPassword,

	/// <summary>The name is not a valid user name.</summary>
	InvalidName,

	/// <summary>Another account already has the name.</summary>
	NameTaken,

	/// <summary>The thing the operation names does not belong to this account.</summary>
	NotFound
}
