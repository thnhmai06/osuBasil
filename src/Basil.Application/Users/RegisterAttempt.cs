using Basil.Domain.Utilities;

namespace Basil.Application.Users;

public sealed record RegisterAttempt(string Username, string AdminKey, Md5 PasswordHash)
	: LoginAttempt(Username, PasswordHash)
{
	public bool ContainAdminKey => !string.IsNullOrEmpty(AdminKey);
}