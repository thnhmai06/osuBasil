using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Users;

/// <summary>The user registry as one connection may act on it: creating accounts.</summary>
public sealed class ScopedUserRegistry : Scoped<UserPermissions>
{
	private readonly UserRegistry _registry;

	/// <summary>Creates a scoped registry for a connection.</summary>
	/// <param name="registry">The registry whose account creation is scoped.</param>
	/// <param name="by">The connection acting.</param>
	internal ScopedUserRegistry(UserRegistry registry, Connection by) : base(by, by.User.EffectivePermissions(by.User.Context.Clock.GetUtcNow()))
	{
		_registry = registry;
	}

	/// <summary>Creates an account with permissions the caller may grant.</summary>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The new account when the result is ok; otherwise why none was created. Granting needs
	///     <see cref="UserPermissions.UserManage" /> and only permissions that are in effect for the caller.
	/// </returns>
	public async Task<(UserRecord? User, Result<UserOutcome> Result)> CreateAccountAsync(UserData data,
		Md5 passwordHash, CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.UserManage | data.Permissions) is { } missing)
			return (null, Result<UserOutcome>.Denied(missing));

		var (user, failure) = await _registry.CreateAccountAsync(data, passwordHash, cancellationToken);
		return failure switch
		{
			null => (user, UserOutcome.Ok),
			RegistrationFailure.InvalidName => (null, UserOutcome.InvalidName),
			_ => (null, UserOutcome.NameTaken)
		};
	}
}
