using Basil.Shared.Structures.Cryptography;


namespace Basil.Application.Users.Persistence;

public record LoginAttempt(string Username, Md5Hash PasswordHash);