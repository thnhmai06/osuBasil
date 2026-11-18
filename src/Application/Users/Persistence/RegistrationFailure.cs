namespace Basil.Application.Users.Persistence;

public enum RegistrationFailure : byte
{
	InvalidName,

	NameTaken,

	Locked
}