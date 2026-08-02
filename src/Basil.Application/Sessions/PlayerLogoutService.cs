using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Sessions;

/// <summary>
///     Performs the coordinated cleanup that runs when a userSession goes offline: leaving the current
///     multiplayer match, tearing down spectator relationships, parting every joined channel with
///     the appropriate broadcast, and removing the session from the registry. Shared by the LOGOUT
///     packet handler and the <c>!reconnect</c> command, both of which force the same cleanup on a
///     session outside the normal logout flow. The LOGOUT packet's one-second login-grace-period
///     check is deliberately absent here because it is specific to that packet rather than part of
///     logout semantics.
/// </summary>
public sealed class PlayerLogoutService(
	IUserSessionRegistry sessionRegistry,
	IChannelRegistry channelRegistry,
	SpectatorService spectatorService,
	MatchMembershipService matchMembership,
	ILogger<PlayerLogoutService> logger)
{
	/// <summary>
	///     Logs <paramref name="userSession" /> out, running each teardown step in order: leaving the
	///     current match under its lock, removing spectator relationships (including BasilBot's own
	///     watch on this userSession), parting all joined channels with membership broadcasts, removing
	///     the session from the registry, and notifying every remaining userSession of the logout.
	/// </summary>
	/// <param name="userSession">The session of the userSession being logged out.</param>
	/// <param name="cancellationToken">A token that cancels the wait on the match lock when the userSession is in a match.</param>
	/// <returns>A task that completes when the logout cleanup has finished.</returns>
	public async Task LogoutAsync(UserSession userSession, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("- User logged out: UserId={UserId} Username={Username}", userSession.Id,
			userSession.Name);

		if (userSession.Match is { } match)
		{
			await match.Lock.WaitAsync(cancellationToken);
			try
			{
				await matchMembership.LeaveAsync(userSession, match, cancellationToken);
			}
			finally
			{
				match.Lock.Release();
			}
		}

		if (userSession.Spectating is { } host) spectatorService.RemoveSpectator(host, userSession);

		// #spec_{userId} is keyed by the persistent user id, stable across relogins — tear down
		// BasilBot's own watch of this departing userSession now, or the channel would be left with a
		// dead member reference until this same user logs back in and re-triggers AddSpectator.
		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
		if (bot is not null) spectatorService.RemoveSpectator(userSession, bot);

		foreach (var channelName in userSession.Channels.ToArray())
		{
			var channel = channelRegistry.GetByName(channelName);
			if (channel is null) continue;

			channel.Part(userSession.Id);
			userSession.LeaveChannel(channelName);

			foreach (var session in sessionRegistry.All)
				if (channel.CanRead(session.Privilege))
					session.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
		}

		sessionRegistry.Remove(userSession);

		if (!userSession.Restricted)
			foreach (var other in sessionRegistry.All)
				other.Enqueue(ServerPacketWriter.Logout(userSession.Id));
	}
}