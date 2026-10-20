using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Application.Services.Sessions;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Users;

/// <summary>Gives access to the accounts' behaviour and announces what happens to them.</summary>
internal sealed class UserService(
	IUserRepository users,
	IRestrictionRepository restrictions,
	IModerationLogRepository moderationLog,
	IRelationshipRepository relationships,
	ILoginRepository logins,
	ICredentialRepository credentials,
	IUserRegistry registry,
	SessionConnectionsService sessions,
	TimeProvider clock,
	IEventPublisher<UserEvent> events) : IUserService, IRoomUsersPort
{
	private readonly UserContext _context = new(clock, events, registry, users, restrictions, moderationLog,
		relationships, logins,
		credentials,
		sessions);

	/// <inheritdoc />
	public User Of(UserRecord user)
	{
		return new User(user, _context);
	}

	ValueTask<bool> IRoomUsersPort.BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken ct)
	{
		return Of(blocker).HasBlockedAsync(who, ct);
	}
}