using Basil.Application.Users.Persistence;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Users.Ports;

internal interface IAccountRules
{
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt, CancellationToken ct);

	Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5 passwordHash,
		UserPrivileges? privileges);

	Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken ct);
}