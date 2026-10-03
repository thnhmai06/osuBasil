using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Users;

/// <summary>Registers new users.</summary>
public sealed class Registration(
	IUserRepository users,
	ICredentialRepository credentials)
{
	/// <summary>Registers a new user with a password.</summary>
	/// <param name="name">The requested username.</param>
	/// <param name="passwordHash">The MD5 of the password.</param>
	/// <param name="adminKey">The MD5 of the administrator key the registrant supplied, or <see langword="null" /> when none was supplied; required only while an administrator key is set.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration was refused.</returns>
	public async Task<(User? User, RegistrationFailure? Failure)> RegisterAsync(string name, Md5 passwordHash,
		Md5? adminKey, CancellationToken cancellationToken = default)
	{
		if (await credentials.GetAdminKeyUpdatedAtAsync(cancellationToken) is not null &&
		    (adminKey is not { } key || !await credentials.VerifyAdminKeyAsync(key, cancellationToken)))
			return (null, RegistrationFailure.WrongAdminKey);

		UserData data;
		try
		{
			data = new UserData { Name = name };
		}
		catch (ArgumentException)
		{
			return (null, RegistrationFailure.InvalidName);
		}

		if (await users.GetByNameAsync(name, cancellationToken) is not null)
			return (null, RegistrationFailure.NameTaken);

		var user = await users.CreateAsync(data, cancellationToken);
		await credentials.CreateOrUpdateAsync(new Credentials(user, passwordHash), cancellationToken);
		return (user, null);
	}
}

/// <summary>The reasons a registration is refused.</summary>
public enum RegistrationFailure : byte
{
	/// <summary>The name is not a valid osu! username.</summary>
	InvalidName,

	/// <summary>Another user already has the name.</summary>
	NameTaken,

	/// <summary>The administrator key is wrong.</summary>
	WrongAdminKey
}