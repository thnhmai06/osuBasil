using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Authentication.Persistence;

public record Credentials(UserRecord User, Md5Hash PasswordHash);