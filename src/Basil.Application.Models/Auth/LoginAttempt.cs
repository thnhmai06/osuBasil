using Basil.Domain.Utilities;

namespace Basil.Application.Models.Auth;

/// <summary>A login attempt as submitted by a client.</summary>
/// <param name="Username">The claimed username.</param>
/// <param name="PasswordHash">The MD5 digest of the claimed password.</param>
public record LoginAttempt(string Username, Md5 PasswordHash);