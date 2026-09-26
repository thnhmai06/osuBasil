using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Registries;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Models.Auth;
using Basil.Application.Models.Sessions;
using Basil.Domain.Auth;
using Basil.Domain.Chat;
using Basil.Domain.Client;
using Basil.Domain.Events;
using Basil.Domain.Users;

namespace Basil.Application.Services.Operations;

/// <summary>
///     Owns a client's connection lifecycle: authenticating and seating a login, and tearing
///     everything down again on disconnect.
/// </summary>
public sealed class Gateway(
	IRepository<string, User> usersByName,
	IRepository<int, User> users,
	ICredentialRepository credentials,
	IPlayerRegistry playerRegistry,
	IChannelRegistry channelRegistry,
	IRepository<string, ChatChannel> channelRepository,
	IRoomRegistry roomRegistry,
	IEventDispatcher dispatcher)
{
	/// <summary>
	///     Authenticates a login attempt and, on success, creates and registers the session and joins
	///     it to every auto-join channel it may read.
	/// </summary>
	/// <param name="attempt">The claimed username and password.</param>
	/// <param name="connection">The transport connection to attach to the new session.</param>
	/// <param name="clientVersion">The osu! client version reported at login.</param>
	/// <param name="fingerprint">The hardware and client fingerprint captured at login.</param>
	/// <param name="utcOffset">The client's UTC offset reported at login.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The login's outcome: a new session on success, or the reason it failed.</returns>
	public async Task<LoginResult> ConnectAsync(LoginAttempt attempt, IClientConnection connection,
		ClientVersion clientVersion, ClientFingerprint fingerprint, int utcOffset,
		CancellationToken cancellationToken = default)
	{
		var user = await usersByName.LoadAsync(UserSafeName.Of(attempt.Username), cancellationToken);
		if (user is null) return LoginResult.Fail(LoginFailure.UnknownUser);
		if (user.DeletedAt is not null) return LoginResult.Fail(LoginFailure.AccountDeleted);

		bool verified;
		try
		{
			verified = await credentials.VerifyAsync(new Credentials(user, attempt.PasswordMd5), cancellationToken);
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
			UserId = user.Id,
			Connection = connection,
			LoginTime = loginTime,
			LastActiveAt = loginTime,
			ClientVersion = clientVersion,
			ClientFingerprint = fingerprint,
			UtcOffset = utcOffset
		};

		if (!playerRegistry.TryAdd(session))
			return LoginResult.Fail(LoginFailure.AlreadyOnline);

		try
		{
			// Live channels are registered in IChannelRegistry at startup; iterate that set rather
			// than searching, since auto-join is a lookup over already-known channels, not a search
			// feature.
			foreach (var (name, channelSession) in channelRegistry.AllByName)
			{
				var channel = await channelRepository.LoadAsync(name, cancellationToken);
				if (channel is null || !channel.AutoJoin) continue;
				if (!user.Privilege.Has(channel.ReadPrivilege)) continue;

				channelSession.Join(channel, user, session);
				await FlushAsync(channelSession, cancellationToken);
			}
		}
		catch
		{
			playerRegistry.Remove(session);
			throw;
		}

		return LoginResult.Success(session);
	}

	/// <summary>
	///     Tears a session down: stops any spectating in either direction, leaves its room, parts
	///     every joined channel, and removes it from the player registry.
	/// </summary>
	/// <param name="session">The session going offline.</param>
	/// <param name="cancellationToken">A token that cancels the teardown.</param>
	public async Task DisconnectAsync(UserSession session, CancellationToken cancellationToken = default)
	{
		try
		{
			if (session is GameSession game)
			{
				if (game.SpectatingUserId is { } hostId &&
				    playerRegistry.AllById.GetValueOrDefault(hostId) is GameSession host)
					game.StopSpectating(host);
				await FlushAsync(game, cancellationToken);

				foreach (var spectatorId in game.SpectatorIds.ToArray())
					if (playerRegistry.AllById.GetValueOrDefault(spectatorId) is GameSession spectator)
					{
						spectator.StopSpectating(game);
						await FlushAsync(spectator, cancellationToken);
					}

				if (game.RoomId is { } roomId)
				{
					var user = await users.LoadAsync(session.UserId, cancellationToken);
					if (user is not null && await roomRegistry.EnterAsync(roomId, cancellationToken) is { } scope)
						await using (scope)
						{
							if (scope.Room.Slots.Find(user) is not null)
								scope.Room.Leave(user);
						}
				}

				game.RoomId = null;
			}

			foreach (var channelName in session.Channels.ToArray())
				if (channelRegistry.AllByName.TryGetValue(channelName, out var channelSession))
				{
					channelSession.Part(session);
					await FlushAsync(channelSession, cancellationToken);
				}
		}
		finally
		{
			playerRegistry.Remove(session);
		}
	}

	private async Task FlushAsync(IHasDomainEvents subject, CancellationToken cancellationToken)
	{
		foreach (var domainEvent in subject.DomainEvents)
			await dispatcher.DispatchAsync(domainEvent, cancellationToken);
		subject.ClearDomainEvents();
	}
}