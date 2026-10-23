using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Domain.Auth;

/// <summary>The stored password of an account.</summary>
/// <param name="User">The account the password belongs to.</param>
/// <param name="PasswordHash">The MD5 hash of the password, as the osu! client sends it.</param>
public record Credentials(UserRecord User, Md5 PasswordHash);