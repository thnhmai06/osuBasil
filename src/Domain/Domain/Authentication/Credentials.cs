using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Authentication;

public record Credentials(UserRecord User, Md5Hash PasswordHash);