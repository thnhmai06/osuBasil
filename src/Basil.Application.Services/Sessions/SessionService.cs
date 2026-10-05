using System.Buffers.Text;
using System.Security.Cryptography;
using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Services.Users;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Sessions;

/// <summary>Opens and closes the connections of online users and changes what they share.</summary>
internal sealed class SessionService(
	UserRegistry registry,
	IChannelService channels,
	TimeProvider time) : ISessionService
{
	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	internal static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

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
	public void SetStatus(BanchoConnection by, PlayerStatus status)
	{
		if (!by.IsOpen || Equals(by.Status, status)) return;

		by.Status = status;
		_events.Writer.TryWrite(new UserConnectionStatusChanged(by, status));
	}

	/// <inheritdoc />
	public void SetAway(UserSession session, string? message)
	{
		session.AwayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	/// <inheritdoc />
	public void SetPmPrivate(UserSession session, bool pmPrivate)
	{
		session.PmPrivate = pmPrivate;
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
		var idleConnections = registry.Sessions
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
	///     connection needs <see cref="Permissions.TournamentObserveRooms" /> in effect.
	/// </remarks>
	internal LoginFailure? Open(Connection connection, IReadOnlyList<Restriction> restrictions)
	{
		using var scope = registry.Enter();

		// An online user's session holds the current copy of the user; a login may have read an older one.
		var user = registry.Find(connection.User)?.User ?? connection.User;
		if (connection.Type is ConnectionType.Tourney &&
		    !user.Value.Permissions.Effective(restrictions, time.GetUtcNow())
			    .Allows(Permissions.TournamentObserveRooms))
			return LoginFailure.NoTourneyPermission;

		var session = registry.Find(connection.User);

		if (session is not null && !connection.Type.AllowsMany() &&
		    session[connection.Type].FirstOrDefault() is { } old)
		{
			if (time.GetUtcNow() - old.LastActiveAt < ReplaceAfterIdle)
				return LoginFailure.AlreadyOnline;

			CloseCore(old, ConnectionCloseReason.Replaced);
			session = registry.Find(connection.User);
		}

		var cameOnline = session is null;
		if (session is null)
		{
			session = new UserSession(connection.User);
			registry.Add(session);
		}

		session.Restrictions = restrictions;
		connection.Session = session;
		session.Add(connection);
		if (connection.Type is not ConnectionType.Tourney)
			channels.Join(session.PmChannel, connection);
		connection.IsOpen = true;
		registry.Index(connection);

		_events.Writer.TryWrite(new UserConnectionOpened(connection, cameOnline));
		return null;
	}

	/// <inheritdoc />
	public int? Announce(Connection by, string text, IReadOnlyCollection<User>? to = null)
	{
		if (!PermissionRules.Allows(by, Permissions.ModeratorAnnounce, time.GetUtcNow())) return null;

		var recipients = registry.Sessions
			.Where(s => to is null || to.Contains(s.User))
			.Select(s => s.Bancho)
			.Where(c => c is not null && c.IsOpen)
			.Cast<BanchoConnection>()
			.ToList();

		if (recipients.Count == 0)
			return 0;

		_events.Writer.TryWrite(new UserNotificationSent(recipients, text));
		return recipients.Count;
	}

	/// <inheritdoc />
	public bool Revoke(Connection by, User user)
	{
		if (!(by is not DelegatedConnection && by.User.Equals(user)) &&
		    !PermissionRules.MayActOn(by, user, Permissions.OwnerManageAccounts, time.GetUtcNow()))
			return false;

		CloseAll(user, ConnectionCloseReason.Revoked);
		return true;
	}

	/// <inheritdoc />
	public (DelegatedConnection? Connection, DelegationFailure? Failure) ActFor(Connection by, User user)
	{
		// ponytail: a leaked token of an account with TournamentActForUsers can act for any online user in rooms and
		// the lobby; add per-user consent if that risk ever matters.
		if (!PermissionRules.Allows(by, Permissions.TournamentActForUsers, time.GetUtcNow()))
			return (null, DelegationFailure.NotPermitted);

		var session = registry.Find(user);
		var connection = session is null
			? null
			: new Connection?[] { session.Bancho, session.Irc, session.Apis.FirstOrDefault(), session.Tourneys.FirstOrDefault() }
				.FirstOrDefault(candidate => candidate is { IsOpen: true });
		return connection is null ? (null, DelegationFailure.UserOffline) : (new DelegatedConnection(connection, by, Permissions.RoomDelegation), null);
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
	internal void CloseAll(User user, ConnectionCloseReason reason)
	{
		var session = registry.Find(user);
		if (session is null) return;

		foreach (var connection in session.Connections.Where(connection => connection.IsOpen).ToList())
			Close(connection, reason);
	}

	/// <summary>Closes the connections of a kind the user may no longer use.</summary>
	/// <param name="session">The session of the user whose permissions or restrictions changed.</param>
	internal void CloseDisallowed(UserSession session)
	{
		if (PermissionRules.Effective(session, time.GetUtcNow()).Allows(Permissions.TournamentObserveRooms)) return;

		foreach (var connection in session.Tourneys.Where(connection => connection.IsOpen).ToList())
			Close(connection, ConnectionCloseReason.Revoked);
	}

	private void CloseCore(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
		registry.Unindex(connection);
		connection.Session.Remove(connection);
		channels.Part(connection.Session.PmChannel, connection);
		if (connection is BanchoConnection bancho)
			channels.Close(bancho.SpectatorChannel);

		var wentOffline = !connection.Session.Connections.Any();
		if (wentOffline)
		{
			registry.Remove(connection.Session);
			channels.Close(connection.Session.PmChannel);
		}

		_events.Writer.TryWrite(new UserConnectionClosed(connection, reason, wentOffline));
	}
}
