namespace Basil.Application.Models.Auth;

/// <summary>A login attempt as submitted by a client.</summary>
/// <param name="Username">The claimed username.</param>
/// <param name="PasswordMd5">The MD5 digest of the claimed password.</param>
public sealed record LoginAttempt(string Username, string PasswordMd5);