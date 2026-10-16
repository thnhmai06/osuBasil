using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Users;

/// <summary>Silences and restricts users and lifts their restrictions.</summary>
internal sealed class UserRestrictionsService(
	IRestrictionRepository restrictions,
	IUserRegistry registry,
	SessionConnectionsService connections,
	UserEventStream events,
	TimeProvider time) : IUserRestrictionsService
{
	/// <inheritdoc />
	public Task<Restriction?> SilenceAsync(Connection by, User user, DateTimeOffset endsAt,
		CancellationToken cancellationToken = default)
	{
		return ImposeAsync(by, user, Permissions.UserSilence, RestrictionData.Silence, endsAt,
			cancellationToken);
	}

	/// <inheritdoc />
	public Task<Restriction?> RestrictAsync(Connection by, User user, Permissions permissions,
		DateTimeOffset? endsAt, CancellationToken cancellationToken = default)
	{
		return ImposeAsync(by, user, Permissions.UserRestrict, permissions, endsAt, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> LiftAsync(Connection by, Restriction restriction,
		CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow();
		var user = restriction.Value.User;
		var silenceOnly = RestrictionData.Silence.Allows(restriction.Value.Permissions);
		if (!MayRestrict(by, user, Permissions.UserRestrict, now) &&
		    !(silenceOnly && MayRestrict(by, user, Permissions.UserSilence, now)))
			return false;
		if (restriction.Value.EndsAt <= now) return true;

		restriction.Value.EndsAt = now;
		await restrictions.UpdateAsync(restriction, cancellationToken);
		registry.Sessions.GetValueOrDefault(user)?.ChangeRestrictions(running => running.RemoveAll(restriction.Equals));

		events.Emit(new UserRestrictionLifted(user, restriction));
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
		if (registry.Sessions.GetValueOrDefault(user) is { } session)
		{
			session.ChangeRestrictions(running => running.Add(restriction));
			connections.CloseDisallowed(session);
		}

		events.Emit(new UserRestricted(user, restriction));
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
}