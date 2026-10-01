using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Users;

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

/// <summary>Registers new users.</summary>
public sealed class Registration(
	IUserRepository users,
	ICredentialRepository credentials,
	IAdminKeyRepository adminKeys)
{
	/// <summary>Registers a new user with a password.</summary>
	/// <param name="name">The requested username.</param>
	/// <param name="passwordHash">The MD5 of the password.</param>
	/// <param name="adminKey">The administrator key.</param>
	/// <param name="cancellationToken">A token that cancels the registration.</param>
	/// <returns>The new user, or why the registration was refused.</returns>
	public async Task<(User? User, RegistrationFailure? Failure)> RegisterAsync(string name, Md5 passwordHash,
		string adminKey, CancellationToken cancellationToken = default)
	{
		if (!await adminKeys.VerifyAsync(adminKey, cancellationToken)) return (null, RegistrationFailure.WrongAdminKey);

		UserData data;
		try
		{
			data = new UserData { Name = name };
		}
		catch (ArgumentException)
		{
			return (null, RegistrationFailure.InvalidName);
		}

		if (await users.FindByNameAsync(name, cancellationToken) is not null)
			return (null, RegistrationFailure.NameTaken);

		var user = await users.AddAsync(data, cancellationToken);
		await credentials.SaveAsync(new Credentials(user, passwordHash), cancellationToken);
		return (user, null);
	}
}