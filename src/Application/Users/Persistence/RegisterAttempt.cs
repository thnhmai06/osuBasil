using Basil.Shared.Structures.Cryptography;


namespace Basil.Application.Users.Persistence;

public sealed record RegisterAttempt(string Username, Md5Hash PasswordHash)
	: LoginAttempt(Username, PasswordHash);