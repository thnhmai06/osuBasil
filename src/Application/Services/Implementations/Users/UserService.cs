using System.Threading.Channels;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Users;

/// <summary>Changes users in ways that have consequences: their permissions and accounts; restrictions are its part.</summary>
internal sealed class UserService(
	IUserRepository users,
	IUserRegistry registry,
	SessionConnectionsService connections,
	TimeProvider time,
	UserEventStream events,
	UserRestrictionsService restrictions) : IUserService
{
	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => events.Reader;

	/// <inheritdoc />
	public IUserRestrictionsService Restrictions => restrictions;

	/// <inheritdoc />
	public async Task<bool> SetPermissionsAsync(Connection by, UserRecord user, Permissions permissions,
		CancellationToken cancellationToken = default)
	{
		if (!PermissionRules.MayActOn(by, user, Permissions.UserManagePermissions, time.GetUtcNow()) ||
		    !PermissionRules.Granted(by).Allows(user.Value.Permissions ^ permissions))
			return false;

		Apply(user, u => u.Permissions = permissions);
		await users.UpdateAsync(user, cancellationToken);
		if (registry.Sessions.GetValueOrDefault(user) is { } session) connections.CloseDisallowed(session);

		events.Emit(new UserPermissionsChanged(user, permissions));
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Connection by, UserRecord user, CancellationToken cancellationToken = default)
	{
		var now = time.GetUtcNow();
		if (!PermissionRules.MayActOn(by, user, Permissions.UserManage, now)) return false;

		Apply(user, u =>
		{
			u.DeletedAt = now;
			u.Permissions = Permissions.None;
		});

		await users.UpdateAsync(user, cancellationToken);
		connections.CloseAll(user, ConnectionCloseReason.Deleted);
		return true;
	}

	/// <summary>Applies a change to a user and syncs it to the online session if one exists.</summary>
	private void Apply(UserRecord user, Action<UserData> change)
	{
		change(user.Value);
		var session = registry.Sessions.GetValueOrDefault(user);
		if (session is not null && !ReferenceEquals(session.User, user))
			change(session.User.Value);
	}
}