using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Client;
using Basil.Domain.Users;
using System.Threading.Channels;

namespace Basil.Application.Services.Users;

/// <summary>Changes users in ways that have consequences.</summary>
internal sealed class UserService(
	IUserRepository users,
	UserRegistry registry,
	ISessionService sessions,
	TimeProvider time) : IUserService
{
	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<bool> SilenceAsync(User user, DateTimeOffset endsAt,
		CancellationToken cancellationToken = default)
	{
		if (user.Id == SystemUserIds.BasilBot) return false;

		user.Value.SilenceEndsAt = endsAt;
		var session = registry.Find(user);
		if (session is not null && !ReferenceEquals(session.User, user))
			session.User.Value.SilenceEndsAt = endsAt;

		await users.CreateOrUpdateAsync(user, cancellationToken);
		_events.Writer.TryWrite(new UserSilenced(user, endsAt));
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> SetPrivilegeAsync(User user, ClientPrivileges privilege,
		CancellationToken cancellationToken = default)
	{
		if (user.Id == SystemUserIds.BasilBot) return false;

		user.Value.Privilege = privilege;
		var session = registry.Find(user);
		if (session is not null && !ReferenceEquals(session.User, user))
			session.User.Value.Privilege = privilege;

		await users.CreateOrUpdateAsync(user, cancellationToken);
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(User user, CancellationToken cancellationToken = default)
	{
		if (user.Id == SystemUserIds.BasilBot) return false;

		var now = time.GetUtcNow();
		user.Value.DeletedAt = now;
		var session = registry.Find(user);
		if (session is not null && !ReferenceEquals(session.User, user))
			session.User.Value.DeletedAt = now;

		await users.CreateOrUpdateAsync(user, cancellationToken);

		if (session is not null)
		{
			var connections = session.Connections.Where(c => c.IsOpen).ToList();
			foreach (var connection in connections)
				sessions.Close(connection, ConnectionCloseReason.Deleted);
		}

		return true;
	}
}