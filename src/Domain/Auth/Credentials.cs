using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

public record Credentials(UserRecord User, Md5 PasswordHash);