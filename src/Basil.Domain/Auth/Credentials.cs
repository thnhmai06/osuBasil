using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Domain.Auth;

public record Credentials(User User, string PasswordMd5)
{
	public string PasswordMd5 { get; init; } = Md5.IsValid(PasswordMd5)
		? PasswordMd5.ToLowerInvariant()
		: throw new ArgumentException("The value must be a valid MD5 hash.", nameof(PasswordMd5));
}