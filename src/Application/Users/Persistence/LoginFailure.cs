namespace Basil.Application.Users.Persistence;

public enum LoginFailure : byte
{
	UnknownUser,

	WrongPassword,

	AccountDeleted,

	TooManySessions,

	NoTourneyPrivilege,

	InvalidToken
}