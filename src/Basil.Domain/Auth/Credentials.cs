using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Domain.Auth;

public record Credentials(User User, Md5 PasswordHash);