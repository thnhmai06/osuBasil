using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Storage.Contracts.Auth;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Users;

/// <summary>Gives access to the accounts' behaviour and announces what happens to them.</summary>
internal sealed class UserService : IUserService, IRoomUsersPort
{
	private readonly UserContext _context;
	private readonly UserEventStream _events;

	public UserService(IUserRepository users, IRestrictionRepository restrictions,
		IModerationLogRepository moderationLog, IRelationshipRepository relationships, ILoginRepository logins, ICredentialRepository credentials,
		IUserRegistry registry,
		SessionConnectionsService sessions,
		TimeProvider clock, UserEventStream events)
	{
		_context = new UserContext(clock, events, registry, users, restrictions, moderationLog, relationships, logins, credentials,
			sessions);
		_events = events;
	}

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

	/// <inheritdoc />
	public User Of(UserRecord user)
	{
		return new User(user, _context);
	}

	ValueTask<bool> IRoomUsersPort.BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken ct) =>
		Of(blocker).HasBlockedAsync(who, ct);
}
