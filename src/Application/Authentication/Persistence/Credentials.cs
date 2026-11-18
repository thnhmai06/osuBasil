using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

public record Credentials(UserRecord User, Md5 PasswordHash);