using System.Net;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Auth;
using Basil.Application.Models.Sessions;
using Basil.Domain.Auth;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>
///     Owns a client's connection lifecycle: authenticating and seating a login, and tearing
///     everything down again on disconnect.
/// </summary>
public sealed class Gateway(
	IRepository<string, User> usersByName,
	ICredentialRepository credentials,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	IChannelRegistry channelRegistry,
	IRoomRegistry roomRegistry)
{
	/// <summary>
	///     Authenticates a login attempt and, on success, creates and registers the session and joins
	///     it to every auto-join channel it may read.
	/// </summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="connection">The transport connection to attach to the new session.</param>
	/// <param name="ip">The IP address the login came from.</param>
	/// <param name="clientVersion">The osu! client version reported at login.</param>
	/// <param name="fingerprint">The hardware and client fingerprint captured at login.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: a new session on success, or the reason it failed.</returns>
	public async Task<LoginResult> ConnectAsync(LoginAttempt attempt, IClientConnection connection,
		IPAddress ip, ClientVersion clientVersion, ClientFingerprint fingerprint, int utcOffset,
		CancellationToken cancellationToken = default)
	{
		var user = await usersByName.LoadAsync(UserSafeName.Of(attempt.Username), cancellationToken);
		if (user is null) return LoginResult.Fail(LoginFailure.UnknownUser);
		if (user.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);

		bool verified;
		try
		{
			verified = await credentials.VerifyAsync(new Credentials(user, attempt.PasswordHash), cancellationToken);
		}
		catch (ArgumentException)
		{
			// The client sent something that is not even a well-formed MD5 digest.
			verified = false;
		}

		if (!verified) return LoginResult.Fail(LoginFailure.WrongPassword);

		var loginTime = DateTimeOffset.UtcNow;
		var session = new GameSession
		{
			Login = new Login(user, ip, clientVersion, fingerprint, loginTime),
			Connection = connection,
			LastActiveAt = loginTime,
			UtcOffset = utcOffset
		};

		if (!gameRegistry.TryAdd(session))
			return LoginResult.Fail(LoginFailure.AlreadyOnline);

		try
		{
			// Live channels are registered in IChannelRegistry at startup; iterate that set rather
			// than searching, since auto-join is a lookup over already-known channels, not a search
			// feature.
			foreach (var channelSession in channelRegistry.AllByName.Values)
			{
				if (!channelSession.Channel.AutoJoin) continue;
				if (!channelSession.CanRead(user)) continue;

				session.Join(channelSession);
			}
		}
		catch
		{
			gameRegistry.Remove(session);
			throw;
		}

		return LoginResult.Success(session);
	}

	/// <summary>
	///     Tears a session down: stops any spectating in either direction, leaves its room, parts
	///     every joined channel, and removes it from the right session registry.
	/// </summary>
	/// <param name="session">The session going offline.</param>
	/// <param name="cancellationToken">A token that cancels the teardown.</param>
	public async Task DisconnectAsync(UserSession session, CancellationToken cancellationToken = default)
	{
		try
		{
			if (session is GameSession game)
			{
				if (game.Spectating is not null)
					game.StopSpectating();

				foreach (var spectator in game.Spectators.ToArray())
					spectator.StopSpectating();

				if (game.Room is { } room && await roomRegistry.EnterAsync(room.Id, cancellationToken) is { } scope)
				{
					await using (scope)
					{
						if (scope.Room.Slots.Find(game) is not null)
							game.LeaveRoom();
					}
				}
			}

			foreach (var channel in session.Channels.ToArray())
				session.Part(channel);
		}
		finally
		{
			if (session is IrcSession irc)
				ircRegistry.Remove(irc);
			else if (session is GameSession gameSession)
				gameRegistry.Remove(gameSession);
		}
	}
}
