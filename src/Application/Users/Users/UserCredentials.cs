using Basil.Application.Users.Capabilities;
using Basil.Domain.Auth;
using Basil.Domain.Common;

namespace Basil.Application.Users.Users;

/// <summary>Owns the password of one account.</summary>
public sealed class UserCredentials
{
	private readonly User _user;

	/// <summary>Creates the credential state for an account.</summary>
	/// <param name="user">The account that owns the password.</param>
	internal UserCredentials(User user)
	{
		_user = user;
	}

	/// <summary>Checks whether a claimed password is the account's password.</summary>
	/// <param name="passwordHash">The MD5 digest of the claimed password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the password is the account's.</returns>
	internal Task<bool> IsPasswordAsync(Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _user.Context.Credentials.IsPasswordAsync(_user.Record, passwordHash, cancellationToken);
	}

	/// <summary>Replaces the account's password.</summary>
	/// <param name="passwordHash">The MD5 digest of the new password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal Task ReplaceAsync(Md5 passwordHash, CancellationToken cancellationToken)
	{
		return _user.Context.Credentials.CreateOrUpdateAsync(new Credentials(_user.Record, passwordHash),
			cancellationToken);
	}
}
