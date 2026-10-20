using System.Buffers.Text;
using System.Security.Cryptography;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Closes connections and keeps them alive.</summary>
internal sealed class SessionConnectionsService(
	IUserRegistry registry,
	SessionContext context,
	SessionEventStream events,
	TimeProvider time) : ISessionConnectionsService, IUserSessionsPort
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
		var idleConnections = registry.Sessions.Values
			.SelectMany(s => s.Connections)
			.Where(c => c.IsOpen && now - c.LastActiveAt > c.Type.IdleTimeout())
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, ConnectionCloseReason.TimedOut);

		return idleConnections.Count;
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
	internal LoginFailure? Open(Connection connection, IReadOnlyList<RestrictionRecord> restrictions)
	{
		using var scope = registry.Enter();

		// An online user's session holds the current copy of the user; a login may have read an older one.
		var user = registry.Sessions.GetValueOrDefault(connection.User)?.User ?? connection.User;
		if (connection.Type is ConnectionType.Tourney &&
		    !user.Value.Permissions.Effective(restrictions, time.GetUtcNow())
			    .Allows(Permissions.RoomObserve))
			return LoginFailure.NoTourneyPermission;

		var session = registry.Sessions.GetValueOrDefault(connection.User);

		if (session is not null && !connection.Type.AllowsMany() &&
		    session[connection.Type].FirstOrDefault() is { } old)
		{
			if (time.GetUtcNow() - old.LastActiveAt < ReplaceAfterIdle)
				return LoginFailure.AlreadyOnline;

			CloseCore(old, ConnectionCloseReason.Replaced);
			session = registry.Sessions.GetValueOrDefault(connection.User);
		}

		var cameOnline = session is null;
		if (session is null)
		{
			session = new UserSession(connection.User, context);
			registry.Add(session);
		}

		session.ChangeRestrictions(_ => [.. restrictions]);
		connection.Session = session;
		session.Add(connection);
		if (connection.Type is not ConnectionType.Tourney)
			session.PmChannel.ScopeTo(connection).Join();
		connection.IsOpen = true;
		registry.Index(connection);

		events.Emit(new UserConnectionOpened(connection, cameOnline));
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
		var session = registry.Sessions.GetValueOrDefault(user);
		if (session is null) return;

		foreach (var connection in session.Connections.Where(connection => connection.IsOpen).ToList())
			Close(connection, reason);
	}

	void IUserSessionsPort.CloseAll(UserRecord user, ConnectionCloseReason reason) => CloseAll(user, reason);

	/// <summary>Closes the connections of a kind the user may no longer use.</summary>
	/// <param name="session">The session of the user whose permissions or restrictions changed.</param>
	internal void CloseDisallowed(UserSession session)
	{
		if (PermissionRules.Effective(session, time.GetUtcNow()).Allows(Permissions.RoomObserve)) return;

		foreach (var connection in session.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, ConnectionCloseReason.Revoked);
	}

	void IUserSessionsPort.CloseDisallowed(UserSession session) => CloseDisallowed(session);

	private void CloseCore(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
		registry.Unindex(connection);
		connection.Session.Remove(connection);
		connection.Session.PmChannel.ScopeTo(connection).Part();
		if (connection is BanchoConnection client)
		{
			client.ReleaseSpectators();
			client.SpectatorChannel.Close();
		}

		var wentOffline = !connection.Session.Connections.Any();
		if (wentOffline)
		{
			registry.Remove(connection.Session);
			connection.Session.PmChannel.Close();
		}

		events.Emit(new UserConnectionClosed(connection, reason, wentOffline));
	}
}
