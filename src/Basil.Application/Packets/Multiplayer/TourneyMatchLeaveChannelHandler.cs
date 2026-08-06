using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles a tournament client's request to leave a match's chat channel.</summary>
/// <remarks>
///     Serves only donator-privileged players. The userSession must currently be registered as a tourney client of the
///     match; actual
///     match participants are not handled here. The userSession is removed from the match's chat channel
///     through <see cref="ChannelMembershipService.Part" /> and unregistered as a tourney client via
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.RemoveTourneyClient" />. A logger
///     correlation scope keyed on the match's database id is opened for the call.
/// </remarks>
public sealed class TourneyMatchLeaveChannelHandler(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ILogger<TourneyMatchLeaveChannelHandler> logger) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.TournamentLeaveMatchChannel;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: tournament channel leaves are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the tournament-leave-channel packet for the given userSession.</summary>
	/// <param name="gameSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();

		if (matchId < 0 || (gameSession.Privilege & UserPrivileges.Donator) == 0) return Task.CompletedTask;

		var match = matchRegistry.GetById(matchId);
		if (match is null || !match.TourneyClients.Contains(gameSession.Id)) return Task.CompletedTask;

		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.Part(gameSession, channel);

		match.RemoveTourneyClient(gameSession.Id);
		return Task.CompletedTask;
	}
}