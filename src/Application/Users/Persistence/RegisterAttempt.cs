using Basil.Domain.Cryptography;

namespace Basil.Application.Users.Persistence;

public sealed record RegisterAttempt(string Username, Md5 PasswordHash)
	: LoginAttempt(Username, PasswordHash);