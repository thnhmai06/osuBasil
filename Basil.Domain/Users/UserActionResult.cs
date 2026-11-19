namespace Basil.Domain.Users;

public enum UserActionResult : byte
{
	Denied = 0,

	Ok = 1,

	NotOutranking,

	SystemAccount,

	Self,

	Deleted,

	WrongPassword,

	NameTaken,

	NotFound,

	Locked,

	NotSelf
}