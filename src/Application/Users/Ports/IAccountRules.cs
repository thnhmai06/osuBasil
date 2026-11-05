using Basil.Application.Users.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Ports;

/// <summary>The account rules the user registry applies when someone registers or signs in.</summary>
internal interface IAccountRules
{
	/// <summary>Checks whether an account may be registered with the given attempt.</summary>
	/// <param name="attempt">The registration the client asked for.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="null" /> when the attempt may be registered; otherwise the reason it may not.</returns>
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt, CancellationToken cancellationToken);

	/// <summary>Creates an account with the given data and its password.</summary>
	/// <param name="data">The data of the new account.</param>
	/// <param name="passwordHash">The password hash of the account.</param>
	/// <param name="permissions">
	///     The permissions of the account, or <see langword="null" /> for the default permissions of new accounts.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the creation.</param>
	/// <returns>The created account, or <see langword="null" /> and the reason when the name is taken.</returns>
	Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5 passwordHash,
		UserPermissions? permissions, CancellationToken cancellationToken);

	/// <summary>Finds the account a login attempt names and checks its password.</summary>
	/// <param name="attempt">The login the client sent.</param>
	/// <param name="cancellationToken">A token that cancels the lookup.</param>
	/// <returns>The account to sign in, or <see langword="null" /> and the reason the attempt fails.</returns>
	Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken cancellationToken);
}