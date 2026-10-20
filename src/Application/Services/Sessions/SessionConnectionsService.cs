using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Application.Services.Users;
using Basil.Application.Storage.Users;
using Basil.Domain.Auth;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Sessions;

/// <summary>Closes connections and keeps them alive.</summary>
internal sealed class SessionConnectionsService(
	IUserRegistry registry,
	IUserRepository users,
	Func<UserRecord, User> userFactory,
	IEventPublisher<UserEvent> events,
	TimeProvider time) : ISessionConnectionsService, IUserConnectionsPort
{
	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	internal static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	/// <inheritdoc />
	public void Close(Connection connection, ConnectionCloseReason reason)
	{
		if (reason is ConnectionCloseReason.LoggedOut && connection is BanchoConnection &&
		    time.GetUtcNow() - connection.Login.Timestamp < IgnoreLogoutWithin)
			return;

		using var scope = registry.Enter();
		CloseCore(connection, reason);
	}

	/// <inheritdoc />
	public void MarkActive(Connection connection)
	{
		if (connection.IsOpen)
			connection.LastActiveAt = time.GetUtcNow();
	}

	/// <inheritdoc />
	public int CloseIdle()
	{
		var now = time.GetUtcNow();
		var idleConnections = registry.Online.Values
			.SelectMany(s => s.Connections)
			.Where(c => c is { IsOpen: true, Type: not ConnectionType.Bot } &&
			            now - c.LastActiveAt > c.Type.IdleTimeout())
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, ConnectionCloseReason.TimedOut);

		return idleConnections.Count;
	}

	/// <inheritdoc />
	public async Task<BotConnection?> ConnectBotAsync(CancellationToken cancellationToken = default)
	{
		var user = await users.GetAsync(BasilBot.Id, cancellationToken);
		if (user is null) return null;

		var online = registry.Online.GetValueOrDefault(user);
		if (online?.Bot is { IsOpen: true } connection) return connection;

		var login = new Login { User = user, Ip = IPAddress.Loopback, Timestamp = time.GetUtcNow() };
		var bot = new BotConnection(login, NewToken());
		return Open(bot, []) is null ? bot : null;
	}

	/// <summary>Opens an authenticated connection, bringing its user online if this is their first.</summary>
	/// <param name="connection">The new connection.</param>
	/// <param name="restrictions">The user's restrictions that have not ended.</param>
	/// <returns><see langword="null" /> on success; otherwise, why the connection was refused.</returns>
	/// <remarks>
	///     A kind that allows one connection per user replaces a connection of the same kind that has been
	///     idle for at least <see cref="ReplaceAfterIdle" />, and refuses the new one otherwise. An osu!tourney
	///     connection needs <see cref="Permissions.RoomObserve" /> in effect.
	/// </remarks>
	internal LoginFailure? Open(Connection connection, IReadOnlyList<Restriction> restrictions)
	{
		using var scope = registry.Enter();

		// The online user holds the current record; a login may have read an older one.
		var record = registry.Online.GetValueOrDefault(connection.Record)?.Record ?? connection.Record;
		if (connection.Type is ConnectionType.Tourney &&
		    !record.Value.Permissions.Effective(restrictions, time.GetUtcNow())
			    .Allows(Permissions.RoomObserve))
			return LoginFailure.NoTourneyPermission;

		var user = registry.Online.GetValueOrDefault(connection.Record);

		if (user is not null && !connection.Type.AllowsMany() &&
		    user[connection.Type].FirstOrDefault() is { } old)
		{
			if (time.GetUtcNow() - old.LastActiveAt < ReplaceAfterIdle)
				return LoginFailure.AlreadyOnline;

			CloseCore(old, ConnectionCloseReason.Replaced);
			user = registry.Online.GetValueOrDefault(connection.Record);
		}

		var cameOnline = user is null;
		user ??= userFactory(connection.Record);
		if (cameOnline)
		{
			registry.Add(user);
		}

		user.ChangeRestrictions(_ => [.. restrictions]);
		connection.User = user;
		user.Add(connection);
		if (connection.Type is not ConnectionType.Tourney)
			user.PmChannel.ScopeTo(connection).Join();
		connection.IsOpen = true;

		// The bot's token is never findable: nobody can present it, and nobody should be able to try.
		if (connection.Type is not ConnectionType.Bot) registry.Index(connection);

		events.Publish(new UserConnectionOpened(connection, cameOnline));
		return null;
	}

	/// <summary>Creates the token of a new connection.</summary>
	/// <returns>32 random bytes, encoded for use in URLs and headers.</returns>
	internal static string NewToken()
	{
		return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
	}

	/// <summary>Closes every open connection of a user.</summary>
	/// <param name="user">The user whose connections are closed.</param>
	/// <param name="reason">Why they are closed.</param>
	internal void CloseAll(UserRecord user, ConnectionCloseReason reason)
	{
		var online = registry.Online.GetValueOrDefault(user);
		if (online is null) return;

		foreach (var connection in online.Connections.Where(connection => connection.IsOpen).ToList())
			Close(connection, reason);
	}

	void IUserConnectionsPort.CloseAll(UserRecord user, ConnectionCloseReason reason)
	{
		CloseAll(user, reason);
	}

	/// <summary>Closes connections of kinds the user may no longer use.</summary>
	/// <param name="user">The online user whose permissions or restrictions changed.</param>
	internal void CloseDisallowed(User user)
	{
		if (PermissionRules.Effective(user, time.GetUtcNow()).Allows(Permissions.RoomObserve)) return;

		foreach (var connection in user.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, ConnectionCloseReason.Revoked);
	}

	void IUserConnectionsPort.CloseDisallowed(User user)
	{
		CloseDisallowed(user);
	}

	private void CloseCore(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
		registry.Unindex(connection);
		connection.User.Remove(connection);
		connection.User.PmChannel.ScopeTo(connection).Part();
		if (connection is BanchoConnection client)
		{
			client.ReleaseSpectators();
			client.SpectatorChannel.Close();
		}

		var wentOffline = !connection.User.Connections.Any();
		if (wentOffline)
		{
			registry.Remove(connection.User);
			connection.User.PmChannel.Close();
		}

		events.Publish(new UserConnectionClosed(connection, reason, wentOffline));
	}
}