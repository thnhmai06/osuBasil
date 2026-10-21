using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;
using Basil.Application.Persistence.Users;

namespace Basil.Application.Runtime.Users;

/// <summary>A user registry scoped to one connection and its authority snapshot.</summary>
public sealed class ScopedUserRegistry
{
	private readonly DateTimeOffset _now;
	private readonly UserRegistry _registry;

	/// <summary>The connection acting.</summary>
	public Connection By { get; }

	/// <summary>Creates a scoped registry for a connection.</summary>
	/// <param name="registry">The registry whose account creation is scoped.</param>
	/// <param name="by">The connection acting.</param>
	internal ScopedUserRegistry(UserRegistry registry, Connection by)
	{
		_registry = registry;
		By = by;
		_now = by.User.Context.Clock.GetUtcNow();
	}

	/// <summary>Creates an account with permissions the caller may grant.</summary>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new user, or why the account could not be created.</returns>
	public Task<(UserRecord? User, RegistrationFailure? Failure)> CreateAccountAsync(UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default)
	{
		if (By.User.Check(Permissions.UserManage, _now) is not Access.Allowed ||
		    !By.User.Record.Value.Permissions.Allows(data.Permissions))
			return Task.FromResult<(UserRecord? User, RegistrationFailure? Failure)>((null,
				RegistrationFailure.NotAuthorized));

		return _registry.CreateAccountAsync(data, passwordHash, cancellationToken);
	}
}