

namespace Basil.Application.Users.Persistence;

public record LoginAttempt(string Username, Md5 PasswordHash);