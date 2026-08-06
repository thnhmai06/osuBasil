using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Sessions;

/// <summary>
///     Performs the coordinated cleanup that runs when a session goes offline. Shared by the LOGOUT
///     packet handler, the <c>!reconnect</c> command, and a real IRC connection's disconnect, both of
///     which force the same cleanup on a session outside the normal logout flow. The LOGOUT packet's
///     one-second login-grace-period check is deliberately absent here because it is specific to that
///     packet rather than part of logout semantics.
/// </summary>
/// <remarks>
///     Game and IRC sessions get different treatment: a <see cref="GameSession" /> leaves its match,
///     tears down spectator relationships, and (if unrestricted) has its logout broadcast to every
///     other <see cref="GameSession" />, since it is the only kind osu! clients ever saw as an online
///     "player" in the first place. An <see cref="IrcSession" /> disconnect never touches match or
///     spectator state and never sends a game presence-offline notification — both would be
///     meaningless for a connection that was chat/commands only. Both kinds part every joined channel
///     through <see cref="ChannelMembershipService.DisconnectFromChannels" />, which applies the
///     shared PART/QUIT rules based on whether the same UserId is still present elsewhere.
/// </remarks>
public sealed class PlayerLogoutService(
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	ChannelMembershipService channelMembership,
	SpectatorService spectatorService,
	MatchMembershipService matchMembership,
	ILogger<PlayerLogoutService> logger)
{
	/// <summary>
	///     Logs <paramref name="userSession" /> out, dispatching to the game or IRC teardown path.
	/// </summary>
	/// <param name="userSession">The session being logged out.</param>
	/// <param name="cancellationToken">A token that cancels the wait on the match lock when the userSession is in a match.</param>
	/// <returns>A task that completes when the logout cleanup has finished.</returns>
	public async Task LogoutAsync(UserSession userSession, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("- User logged out: UserId={UserId} Username={Username}", userSession.Id,
			userSession.Name);

		switch (userSession)
		{
			case GameSession game:
				await LogoutGameSessionAsync(game, cancellationToken);
				break;
			case IrcSession irc:
				LogoutIrcSession(irc);
				break;
		}
	}

	private async Task LogoutGameSessionAsync(GameSession game, CancellationToken cancellationToken)
	{
		if (game.Match is { } match)
		{
			await match.Lock.WaitAsync(cancellationToken);
			try
			{
				await matchMembership.LeaveAsync(game, match, cancellationToken);
			}
			finally
			{
				match.Lock.Release();
			}
		}

		if (game.Spectating is { } host) spectatorService.RemoveSpectator(host, game);

		// #spec_{userId} is keyed by the persistent user id, stable across relogins — tear down
		// BasilBot's own watch of this departing userSession now, or the channel would be left with a
		// dead member reference until this same user logs back in and re-triggers AddSpectator.
		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is not null) spectatorService.RemoveSpectator(game, bot);

		channelMembership.DisconnectFromChannels(game, "Logged out");
		gameRegistry.Remove(game);

		if (!game.Restricted)
			foreach (var other in gameRegistry.All)
				other.Enqueue(ServerPacketWriter.Logout(game.Id));
	}

	private void LogoutIrcSession(IrcSession irc)
	{
		channelMembership.DisconnectFromChannels(irc, "Connection closed");
		ircRegistry.Remove(irc);
	}
}
