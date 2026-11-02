using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>The user registry as one connection may act on it: creating accounts.</summary>
public sealed class ScopedUserRegistry : Scoped<UserPermissions, Connection>
{
	private readonly UserRegistry _registry;

	/// <summary>Creates a scoped registry for a connection.</summary>
	/// <param name="registry">The registry whose account creation is scoped.</param>
	/// <param name="actor">The connection acting.</param>
	internal ScopedUserRegistry(UserRegistry registry, Connection actor) : base(actor,
		actor.EffectivePermissions(actor.User.Context.Clock.GetUtcNow()))
	{
		_registry = registry;
	}

	/// <summary>Creates an account with permissions the caller may grant.</summary>
	/// <param name="data">The account data.</param>
	/// <param name="passwordHash">The MD5 digest of the password.</param>
	/// <param name="permissions">
	///     The permissions of the account, or <see langword="null" /> for the default permissions of new accounts.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The new account when the result is ok; otherwise why none was created. Granting needs
	///     <see cref="UserPermissions.UserManage" /> and only permissions that are in effect for the caller.
	/// </returns>
	public async Task<(UserRecord? User, Result<UserOutcome> Result)> CreateAccountAsync(UserData data,
		Md5 passwordHash, UserPermissions? permissions, CancellationToken cancellationToken = default)
	{
		var granted = permissions ??
		             (await Actor.User.Context.Settings.GetAsync(cancellationToken)).Accounts.DefaultPermissions;
		if (Require(UserPermissions.UserManage | granted) is { } missing)
			return (null, Result<UserOutcome>.Denied(missing));

		var (user, failure) = await _registry.CreateAccountAsync(data, passwordHash, granted, cancellationToken);
		return failure switch
		{
			null => (user, UserOutcome.Ok),
			RegistrationFailure.InvalidName => (null, UserOutcome.InvalidName),
			_ => (null, UserOutcome.NameTaken)
		};
	}
}