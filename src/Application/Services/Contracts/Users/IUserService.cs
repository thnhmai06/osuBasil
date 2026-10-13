using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>Changes users in ways that have consequences: their permissions and accounts; restrictions are its part.</summary>
/// <remarks>
///     Every operation aimed at another user requires the caller to outrank them: the caller's granted permissions must
///     include every permission granted to the target and at least one more.
/// </remarks>
public interface IUserService : IEventPublisher<UserEvent>
{
	/// <summary>Silences, restrictions and lifting them.</summary>
	IUserRestrictionsService Restrictions { get; }

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