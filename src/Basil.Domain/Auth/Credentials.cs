using Basil.Domain.Users;

namespace Basil.Domain.Auth;

public record Credentials(User User, string PasswordMd5);