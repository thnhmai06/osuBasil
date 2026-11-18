namespace Basil.Domain.Users;

public enum UserOutcome : byte
{
	Denied = 0,

	Ok = 1,

	NotOutranking,

	SystemAccount,

	InvalidSilence,

	Self,

	Deleted,

	WrongPassword,

	InvalidName,

	NameTaken,

	NotFound,

	Locked,

	NotSelf,

	InvalidCountry,

	InvalidImage,

	DimensionsTooLarge,

	TooLarge
}