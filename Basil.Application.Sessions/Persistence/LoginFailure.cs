namespace Basil.Application.Sessions.Persistence;

public enum LoginFailure : byte
{
	UnknownUser,

	WrongPassword,

	AccountDeleted,

	AlreadyOnline,

	NoTourneyPrivilege,

	InvalidToken
}