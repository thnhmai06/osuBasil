using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Authentication;

/// <summary>The account a sign-in names together with the password hash it presented.</summary>
/// <param name="User">The account the sign-in names.</param>
/// <param name="PasswordHash">The MD5 hash of the password presented.</param>
public record Credentials(UserRecord User, Md5Hash PasswordHash);