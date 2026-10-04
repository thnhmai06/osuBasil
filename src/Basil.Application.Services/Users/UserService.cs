using System.Threading.Channels;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Client;
using Basil.Domain.Users;

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

		Apply(user, u => u.SilenceEndsAt = endsAt);

		await users.CreateOrUpdateAsync(user, cancellationToken);
		_events.Writer.TryWrite(new UserSilenced(user, endsAt));
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> SetPrivilegeAsync(User user, ClientPrivileges privilege,
		CancellationToken cancellationToken = default)
	{
		if (user.Id == SystemUserIds.BasilBot) return false;

		Apply(user, u => u.Privilege = privilege);

		await users.CreateOrUpdateAsync(user, cancellationToken);
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(User user, CancellationToken cancellationToken = default)
	{
		if (user.Id == SystemUserIds.BasilBot) return false;

		var now = time.GetUtcNow();
		Apply(user, u =>
		{
			u.DeletedAt = now;
			u.Privilege = ClientPrivileges.None;
		});

		await users.CreateOrUpdateAsync(user, cancellationToken);

		var session = registry.Find(user);
		if (session is not null)
		{
			var connections = session.Connections.Where(c => c.IsOpen).ToList();
			foreach (var connection in connections)
				sessions.Close(connection, ConnectionCloseReason.Deleted);
		}

		return true;
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