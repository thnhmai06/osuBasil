using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Persistence;

public record LoginAttempt(string Username, Md5Hash PasswordHash);