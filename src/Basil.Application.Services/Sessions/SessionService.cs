using System.Net;
using System.Threading.Channels;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Storage.Sessions;
using Basil.Application.Storage.Users;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Services.Sessions;

/// <summary>Opens and closes the connections of online users and changes what they share.</summary>
internal sealed class SessionService(
	UserRegistry registry,
	IChannelService channels,
	IUserRepository users,
	TimeProvider time) : ISessionService
{
	/// <summary>How long a connection must be idle before a new login of the same kind replaces it.</summary>
	internal static readonly TimeSpan ReplaceAfterIdle = TimeSpan.FromSeconds(10);

	/// <summary>A logout an osu! client sends this soon after login is ignored.</summary>
	internal static readonly TimeSpan IgnoreLogoutWithin = TimeSpan.FromSeconds(1);

	/// <summary>How long a client connection may send nothing before it is closed.</summary>
	internal static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(300);

	/// <summary>The default name for BasilBot.</summary>
	internal const string BotName = "BasilBot";

	/// <summary>The default country for BasilBot.</summary>
	internal const Country BotCountry = Country.Vn;

	/// <summary>The default privileges for BasilBot.</summary>
	internal static readonly ClientPrivileges BotPrivilege =
		ClientPrivileges.Player | ClientPrivileges.Moderator | ClientPrivileges.Supporter;

	private readonly Channel<UserEvent> _events = Channel.CreateUnbounded<UserEvent>();

	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => _events.Reader;

	/// <summary>Opens an authenticated connection, bringing its user online if this is their first.</summary>
	/// <param name="connection">The new connection.</param>
	/// <returns><see langword="null" /> on success; otherwise, why the connection was refused.</returns>
	/// <remarks>
	///     A kind that allows one connection per user replaces a connection of the same kind that has been
	///     idle for at least <see cref="ReplaceAfterIdle" />, and refuses the new one otherwise. osu!tourney
	///     connections require the Player and Supporter privileges.
	/// </remarks>
	internal LoginFailure? Open(Connection connection)
	{
		using var scope = registry.Enter();

		if (connection.Type is ConnectionType.Tourney &&
		    !connection.User.Value.Privilege.Has(ClientPrivileges.Player | ClientPrivileges.Supporter))
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

		connection.Session = session;
		session.Add(connection);
		if (connection.Type is not ConnectionType.Tourney)
			channels.Join(session.PmChannel, connection);
		connection.IsOpen = true;

		_events.Writer.TryWrite(new UserConnectionOpened(connection, cameOnline));
		return null;
	}

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
			.Where(c => c is not BotConnection && c.IsOpen && now - c.LastActiveAt > IdleTimeout)
			.ToList();

		foreach (var connection in idleConnections)
			Close(connection, ConnectionCloseReason.TimedOut);

		return idleConnections.Count;
	}

	/// <inheritdoc />
	public int Announce(string text, IReadOnlyCollection<User>? to = null)
	{
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
	public async Task<BotConnection> OpenBotAsync(CancellationToken cancellationToken = default)
	{
		var existingBot = registry.Sessions
			.Select(s => s.Bot)
			.FirstOrDefault(c => c is not null && c.IsOpen);

		if (existingBot is not null)
			return existingBot;

		var bot = await users.GetAsync(SystemUserIds.BasilBot, cancellationToken);
		if (bot is null)
		{
			bot = new User
			{
				Id = SystemUserIds.BasilBot,
				Value = new UserData
				{
					Name = BotName,
					Country = BotCountry,
					Privilege = BotPrivilege
				}
			};
			await users.CreateOrUpdateAsync(bot, cancellationToken);
		}

		var login = new Login
		{
			User = bot,
			Ip = IPAddress.Loopback,
			Timestamp = time.GetUtcNow()
		};
		var connection = new BotConnection(login);
		var failure = Open(connection);
		if (failure is not null)
			throw new InvalidOperationException($"BasilBot could not come online: {failure}");

		return connection;
	}

	private void CloseCore(Connection connection, ConnectionCloseReason reason)
	{
		if (!connection.IsOpen) return;

		connection.IsOpen = false;
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