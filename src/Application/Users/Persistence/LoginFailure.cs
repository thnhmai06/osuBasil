namespace Basil.Application.Users.Persistence;

public enum LoginFailure : byte
{
	UnknownUser,

	WrongPassword,

	AccountDeleted,

	AlreadyOnline,

	NoTourneyPrivilege,

	InvalidToken
}