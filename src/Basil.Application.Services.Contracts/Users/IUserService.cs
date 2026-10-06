using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>Changes users in ways that have consequences: their permissions, restrictions and accounts.</summary>
/// <remarks>
///     Every operation aimed at another user requires the caller to outrank them: the caller's granted permissions must
///     include every permission granted to the target and at least one more.
/// </remarks>
public interface IUserService : IEventPublisher<UserEvent>
{
	/// <summary>Silences a user until a given time.</summary>
	/// <param name="by">The connection acting; its user needs <see cref="Permissions.ModeratorSilence" />.</param>
	/// <param name="user">The user to silence.</param>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction that silences the user, or <see langword="null" /> when the caller may not silence them.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="endsAt" /> is in the past.</exception>
	/// <remarks>
	///     A silence is a restriction of <see cref="Permissions.SuspendedBySilence" />. Announces
	///     <see cref="UserRestricted" />.
	/// </remarks>
	Task<Restriction?> SilenceAsync(Connection by, User user, DateTimeOffset endsAt,
		CancellationToken cancellationToken = default);

	/// <summary>Suspends some of a user's permissions.</summary>
	/// <param name="by">The connection acting; its user needs <see cref="Permissions.ModeratorRestrict" />.</param>
	/// <param name="user">The user to restrict.</param>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until it is lifted.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new restriction, or <see langword="null" /> when the caller may not restrict the user.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="permissions" /> is none, or <paramref name="endsAt" /> is in the past.
	/// </exception>
	/// <remarks>
	///     Connections of a kind the user may no longer use are closed as <see cref="ConnectionCloseReason.Revoked" />.
	///     Announces <see cref="UserRestricted" />.
	/// </remarks>
	Task<Restriction?> RestrictAsync(Connection by, User user, Permissions permissions, DateTimeOffset? endsAt,
		CancellationToken cancellationToken = default);

	/// <summary>Ends a restriction now.</summary>
	/// <param name="by">
	///     The connection acting; its user needs <see cref="Permissions.ModeratorRestrict" />, or
	///     <see cref="Permissions.ModeratorSilence" /> to lift a silence.
	/// </param>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if the restriction has ended, including when it had already ended; otherwise,
	///     <see langword="false" /> when the caller may not lift it.
	/// </returns>
	/// <remarks>Announces <see cref="UserRestrictionLifted" /> when the restriction was still running.</remarks>
	Task<bool> LiftAsync(Connection by, Restriction restriction, CancellationToken cancellationToken = default);

	/// <summary>Replaces the permissions granted to a user.</summary>
	/// <param name="by">
	///     The connection acting; its user needs <see cref="Permissions.OwnerManagePermissions" /> and may only add or
	///     remove permissions it holds itself.
	/// </param>
	/// <param name="user">The user whose permissions are replaced.</param>
	/// <param name="permissions">The new permissions.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the user was changed; <see langword="false" /> when the caller may not.</returns>
	/// <remarks>
	///     Changing one's own permissions needs no outranking. Connections of a kind the user may no longer use are closed
	///     as <see cref="ConnectionCloseReason.Revoked" />. Announces <see cref="UserPermissionsChanged" />.
	/// </remarks>
	Task<bool> SetPermissionsAsync(Connection by, User user, Permissions permissions,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a user's account, keeping its record.</summary>
	/// <param name="by">The connection acting; its user needs <see cref="Permissions.OwnerManageAccounts" />.</param>
	/// <param name="user">The user to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the user was deleted; <see langword="false" /> when the caller may not.</returns>
	/// <remarks>The user loses every permission and their open connections are closed.</remarks>
	Task<bool> DeleteAsync(Connection by, User user, CancellationToken cancellationToken = default);
}