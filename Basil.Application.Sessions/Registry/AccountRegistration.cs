using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Logic;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Registry;

/// <summary>Creates accounts, for a person who signs up and for an administrator.</summary>
public sealed class AccountRegistration
{
	private readonly AccountRules _accounts;

	internal AccountRegistration(AccountRules accounts)
	{
		_accounts = accounts;
	}

	/// <summary>Checks whether an account could be registered with the attempt, without creating it.</summary>
	/// <returns>Why it could not, or null when it could.</returns>
	public Task<RegistrationFailure?> CheckAsync(RegisterAttempt attempt, CancellationToken ct = default)
	{
		return _accounts.CheckRegistrationAsync(attempt, ct);
	}

	internal async Task<(UserRecord? User, RegistrationFailure? Failure)> RegisterAsync(RegisterAttempt attempt,
		CancellationToken ct = default)
	{
		var failure = await CheckAsync(attempt, ct);
		if (failure is not null) return (null, failure);

		return await _accounts.CreateAsync(new UserData { Name = attempt.Username }, attempt.PasswordHash, null);
	}

	internal async Task<(UserRecord? User, UserActionResult Outcome)> CreateAsync(UserData data, Md5Hash passwordHash,
		UserPrivileges? privileges)
	{
		var (user, failure) = await _accounts.CreateAsync(data, passwordHash, privileges);
		return failure switch
		{
			null => (user, UserActionResult.Ok),
			_ => (null, UserActionResult.NameTaken)
		};
	}
}
