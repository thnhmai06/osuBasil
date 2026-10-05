using Basil.Domain.Utilities;

namespace Basil.Application.Contracts.Users;

/// <summary>A registration attempt as submitted by a client.</summary>
/// <param name="Username">The requested username.</param>
/// <param name="PasswordHash">The MD5 digest of the requested password.</param>
public sealed record RegisterAttempt(string Username, Md5 PasswordHash)
	: LoginAttempt(Username, PasswordHash);