using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>The user registry as one connection may act on it: creating accounts.</summary>
public readonly struct ScopedUserRegistry
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the permissions the actor held when the scope was created.</summary>
	public UserPermissions Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPermissions? Require(UserPermissions required)
	{
		return ScopedRights.Require(Rights, required);
	}

	private readonly UserRegistry _registry;

	/// <summary>Creates a scoped registry for a connection.</summary>
	/// <param name="registry">The registry whose account creation is scoped.</param>
	/// <param name="actor">The connection acting.</param>
	internal ScopedUserRegistry(UserRegistry registry, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePermissions(actor.User.Context.Clock.GetUtcNow());
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