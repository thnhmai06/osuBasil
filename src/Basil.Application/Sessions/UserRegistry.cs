using System.Collections.Concurrent;
using System.Threading.Channels;
using Basil.Application.Common.Events;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>Who is online: opens and closes connections and announces every change.</summary>
public sealed class UserRegistry(TimeProvider time) : IEventPublisher<UserEvent>
{
	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	public static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();
	private readonly ConcurrentDictionary<User, UserSession> _sessions = new();
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <summary>Gets every online session.</summary>
	public IEnumerable<UserSession> Sessions => _sessions.Values;

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

	/// <summary>Finds the online session of a user.</summary>
	/// <param name="user">The user to look up.</param>
	/// <returns>The user's session, or <see langword="null" /> when the user is offline.</returns>
	public UserSession? Find(User user)
	{
		return _sessions.GetValueOrDefault(user);
	}

	/// <summary>Finds the spectator channel a connection is spectating.</summary>
	/// <param name="connection">The connection to look up.</param>
	/// <returns>The channel of the player being spectated, or <see langword="null" /> when the connection is not spectating.</returns>
	public SpectatorChannelSession? Watching(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _sessions.Values
			.Select(session => session.Bancho?.SpectatorChannel)
			.FirstOrDefault(channel => channel is not null && !ReferenceEquals(channel.Host, connection) &&
			                           channel.Members.Contains(connection));
	}

	/// <summary>Opens an authenticated connection, bringing its user online if this is their first.</summary>
	/// <param name="connection">The new connection.</param>
	/// <returns><see langword="null" /> on success; otherwise, why the connection was refused.</returns>
	/// <remarks>
	///     A kind that allows one connection per user replaces a connection of the same kind that has been
	///     idle for at least <see cref="ReplaceAfterIdle" />, and refuses the new one otherwise. osu!tourney
	///     connections require the Player and Supporter privileges.
	/// </remarks>
	public LoginFailure? OpenConnection(Connection connection)
	{
		lock (_sync)
		{
			if (connection.Type is ConnectionType.Tourney &&
			    !connection.User.Value.Privilege.Has(ClientPrivileges.Player | ClientPrivileges.Supporter))
				return LoginFailure.NoTourneyPermission;

			var session = _sessions.GetValueOrDefault(connection.User);

			if (session is not null && !connection.Type.AllowsMany() &&
			    session[connection.Type].FirstOrDefault() is { } old)
			{
				DateTimeOffset? lastActive = old switch
				{
					BanchoConnection b => b.LastActiveAt,
					IrcConnection i => i.LastActiveAt,
					_ => null
				};

				if (lastActive is null || time.GetUtcNow() - lastActive < ReplaceAfterIdle)
					return LoginFailure.AlreadyOnline;

				Close(old, ConnectionCloseReason.Replaced);
				session = _sessions.GetValueOrDefault(connection.User);
			}

			var cameOnline = session is null;
			session ??= new UserSession(connection.User, time);
			_sessions[connection.User] = session;
			connection.Session = session;
			session.Add(connection);
			if (connection.Type is not ConnectionType.Tourney)
				session.PmChannel.Join(connection);
			connection.IsOpen = true;

			_events.Writer.TryWrite(new UserConnectionOpened(connection, cameOnline));
			return null;
		}
	}

	/// <summary>Closes a connection, taking its user offline if it was their last one.</summary>
	/// <param name="connection">The connection to close.</param>
	/// <param name="reason">Why the connection is closed.</param>
	/// <remarks>Closing a connection that is already closed does nothing.</remarks>
	public void CloseConnection(Connection connection, ConnectionCloseReason reason)
	{
		lock (_sync)
		{
			Close(connection, reason);
		}
	}

	/// <summary>Records the presence status an osu! client reported.</summary>
	/// <param name="by">The client connection that reported it.</param>
	/// <param name="status">The reported status.</param>
	/// <remarks>Reporting the status the client already has, or reporting from a closed connection, does nothing.</remarks>
	public void SetStatus(BanchoConnection by, PlayerStatus status)
	{
		if (!by.IsOpen || Equals(by.Status, status)) return;

		by.Status = status;
		_events.Writer.TryWrite(new UserConnectionStatusChanged(by, status));
	}

	/// <summary>Silences a user until a given time.</summary>
	/// <param name="user">The user to silence.</param>
	/// <param name="endsAt">When the silence ends.</param>
	public void Silence(User user, DateTimeOffset endsAt)
	{
		user.Value.SilenceEndsAt = endsAt;
		_events.Writer.TryWrite(new UserSilenced(user, endsAt));
	}

	/// <summary>Announces that a user's statistics changed.</summary>
	/// <param name="user">The user whose statistics changed.</param>
	public void ReportStatsChanged(User user)
	{
		_events.Writer.TryWrite(new UserStatsChanged(user));
	}

	private void Close(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
		connection.Session.Remove(connection);
		connection.Session.PmChannel.Part(connection);
		Watching(connection)?.StopSpectating(connection);
		if (connection is BanchoConnection bancho)
			bancho.SpectatorChannel.Close();

		var wentOffline = !connection.Session.Connections.Any();
		if (wentOffline)
		{
			_sessions.TryRemove(new KeyValuePair<User, UserSession>(connection.User, connection.Session));
			connection.Session.PmChannel.Close();
		}

		_events.Writer.TryWrite(new UserConnectionClosed(connection, reason, wentOffline));
	}
}