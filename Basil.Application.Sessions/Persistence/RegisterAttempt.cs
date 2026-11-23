using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Persistence;

public sealed record RegisterAttempt(string Username, Md5Hash PasswordHash)
	: LoginAttempt(Username, PasswordHash);