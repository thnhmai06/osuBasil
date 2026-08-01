using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Sessions;

/// <summary>
///     Performs the coordinated cleanup that runs when a player goes offline: leaving the current
///     multiplayer match, tearing down spectator relationships, parting every joined channel with
///     the appropriate broadcast, and removing the session from the registry. Shared by the LOGOUT
///     packet handler and the <c>!reconnect</c> command, both of which force the same cleanup on a
///     session outside of the normal logout flow. The LOGOUT packet's one-second login-grace-period
///     check is deliberately absent here because it is specific to that packet rather than part of
///     logout semantics.
/// </summary>
public sealed class PlayerLogoutService(
	IPlayerSessionRegistry sessionRegistry,
	IChannelRegistry channelRegistry,
	SpectatorService spectatorService,
	MatchMembershipService matchMembership,
	ILogger<PlayerLogoutService> logger)
{
	/// <summary>
	///     Logs <paramref name="player" /> out, running each teardown step in order: leaving the
	///     current match under its lock, removing spectator relationships (including BasilBot's own
	///     watch on this player), parting all joined channels with membership broadcasts, removing
	///     the session from the registry, and notifying every remaining player of the logout.
	/// </summary>
	/// <param name="player">The session of the player being logged out.</param>
	/// <param name="cancellationToken">A token that cancels the wait on the match lock when the player is in a match.</param>
	/// <returns>A task that completes when the logout cleanup has finished.</returns>
	public async Task LogoutAsync(PlayerSession player, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("- User logged out: UserId={UserId} Username={Username}", player.Id, player.Name);

		if (player.Match is { } match)
		{
			await match.Lock.WaitAsync(cancellationToken);
			try
			{
				await matchMembership.LeaveAsync(player, match, cancellationToken);
			}
			finally
			{
				match.Lock.Release();
			}
		}

		if (player.Spectating is { } host) spectatorService.RemoveSpectator(host, player);

		// #spec_{userId} is keyed by the persistent user id, stable across relogins — tear down
		// BasilBot's own watch of this departing player now, or the channel would be left with a
		// dead member reference until this same user logs back in and re-triggers AddSpectator.
		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
		if (bot is not null) spectatorService.RemoveSpectator(player, bot);

		foreach (var channelName in player.Channels.ToArray())
		{
			var channel = channelRegistry.GetByName(channelName);
			if (channel is null) continue;

			channel.Part(player.Id);
			player.LeaveChannel(channelName);

			foreach (var session in sessionRegistry.All)
				if (channel.CanRead(session.Privilege))
					session.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
		}

		sessionRegistry.Remove(player);

		if (!player.Restricted)
			foreach (var other in sessionRegistry.All)
				other.Enqueue(ServerPacketWriter.Logout(player.Id));
	}
}