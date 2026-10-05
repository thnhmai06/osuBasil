using System.Threading.Channels;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Services.Sessions;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Users;

/// <summary>Changes users in ways that have consequences: their permissions, restrictions and accounts.</summary>
internal sealed class UserService(
	IUserRepository users,
	IRestrictionRepository restrictions,
	UserRegistry registry,
	SessionService sessions,
	TimeProvider time) : IUserService
{
	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

	/// <inheritdoc />
	public Task<Restriction?> SilenceAsync(Connection by, User user, DateTimeOffset endsAt,
		CancellationToken cancellationToken = default)
	{
		return ImposeAsync(by, user, Permissions.ModeratorSilence, Permissions.SuspendedBySilence, endsAt,
			cancellationToken);
	}

	/// <inheritdoc />
	public Task<Restriction?> RestrictAsync(Connection by, User user, Permissions permissions,
		DateTimeOffset? endsAt, CancellationToken cancellationToken = default)
	{
		return ImposeAsync(by, user, Permissions.ModeratorRestrict, permissions, endsAt, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> LiftAsync(Connection by, Restriction restriction,
		CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow();
		var user = restriction.Value.User;
		var silenceOnly = Permissions.SuspendedBySilence.Allows(restriction.Value.Permissions);
		if (!MayRestrict(by, user, Permissions.ModeratorRestrict, now) &&
		    !(silenceOnly && MayRestrict(by, user, Permissions.ModeratorSilence, now)))
			return false;
		if (restriction.Value.EndsAt <= now) return true;

		restriction.Value.EndsAt = now;
		await restrictions.CreateOrUpdateAsync(restriction, cancellationToken);
		if (registry.Find(user) is { } session)
		{
			using var scope = registry.Enter();
			session.Restrictions = session.Restrictions.Where(running => !running.Equals(restriction)).ToList();
		}

		_events.Writer.TryWrite(new UserRestrictionLifted(user, restriction));
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> SetPermissionsAsync(Connection by, User user, Permissions permissions,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.MayActOn(by, user, Permissions.OwnerManagePermissions, time.GetUtcNow()) ||
		    !by.User.Value.Permissions.Allows(user.Value.Permissions ^ permissions))
			return false;

		Apply(user, u => u.Permissions = permissions);
		await users.CreateOrUpdateAsync(user, cancellationToken);
		if (registry.Find(user) is { } session) sessions.CloseDisallowed(session);

		_events.Writer.TryWrite(new UserPermissionsChanged(user, permissions));
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Connection by, User user, CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow();
		if (!PermissionRules.MayActOn(by, user, Permissions.OwnerManageAccounts, now)) return false;

		Apply(user, u =>
		{
			u.DeletedAt = now;
			u.Permissions = Permissions.None;
		});

		await users.CreateOrUpdateAsync(user, cancellationToken);
		sessions.CloseAll(user, ConnectionCloseReason.Deleted);
		return true;
	}

	/// <summary>Suspends some of a user's permissions, if the caller may.</summary>
	/// <param name="by">The connection acting.</param>
	/// <param name="user">The user to restrict.</param>
	/// <param name="required">The permission the caller needs.</param>
	/// <param name="suspended">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until it is lifted.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new restriction, or <see langword="null" /> when the caller may not restrict the user.</returns>
	private async Task<Restriction?> ImposeAsync(Connection by, User user, Permissions required,
		Permissions suspended, DateTimeOffset? endsAt, CancellationToken cancellationToken)
	{
		var now = time.GetUtcNow();
		if (!MayRestrict(by, user, required, now)) return null;

		var data = new RestrictionData { User = user, Permissions = suspended, StartsAt = now, EndsAt = endsAt };
		var restriction = await restrictions.CreateAsync(data, cancellationToken);
		if (registry.Find(user) is { } session)
		{
			using (registry.Enter())
				session.Restrictions = [.. session.Restrictions, restriction];
			sessions.CloseDisallowed(session);
		}

		_events.Writer.TryWrite(new UserRestricted(user, restriction));
		return restriction;
	}

	/// <summary>
	///     Gets a value that indicates whether a connection may restrict or lift a restriction of a user: never their own,
	///     so a restricted moderator cannot free themselves.
	/// </summary>
	private static bool MayRestrict(Connection by, User user, Permissions required, DateTimeOffset now)
	{
		return !by.User.Equals(user) && PermissionRules.MayActOn(by, user, required, now);
	}

	/// <summary>Applies a change to a user and syncs it to the online session if one exists.</summary>
	private void Apply(User user, Action<UserData> change)
	{
		change(user.Value);
		var session = registry.Find(user);
		if (session is not null && !ReferenceEquals(session.User, user))
			change(session.User.Value);
	}
}
