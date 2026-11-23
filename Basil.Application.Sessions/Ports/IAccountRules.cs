using Basil.Application.Sessions.Persistence;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Sessions.Ports;

internal interface IAccountRules
{
	Task<RegistrationFailure?> CheckRegistrationAsync(RegisterAttempt attempt, CancellationToken ct);

	Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAsync(UserData data, Md5Hash passwordHash,
		UserPrivileges? privileges);

	Task<(UserRecord? User, LoginFailure? Failure)> FindLoginUserAsync(LoginAttempt attempt,
		CancellationToken ct);
}