namespace Basil.Application.Contracts.Users;

/// <summary>A registration attempt as submitted by a client.</summary>
/// <param name="Username">The requested username.</param>
/// <param name="PasswordHash">The MD5 digest of the requested password.</param>
/// <param name="AdminKey">
///     The MD5 digest of the administrator key the registrant supplied, or <see langword="null" /> when
///     none was supplied.
/// </param>
public sealed record RegisterAttempt(string Username, Md5 PasswordHash, Md5? AdminKey)
	: LoginAttempt(Username, PasswordHash);