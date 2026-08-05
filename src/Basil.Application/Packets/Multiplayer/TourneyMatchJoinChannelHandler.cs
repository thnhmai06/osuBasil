using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles a tournament client's request to join a match's chat channel.</summary>
/// <remarks>
///     Serves only donator-privileged players. A userSession who already occupies a slot in the match is left alone rather
///     than
///     treated as a tourney client. Otherwise, the userSession joins the match's chat channel through
///     <see cref="ChannelMembershipService.Join" />, and when the join succeeds, the userSession is registered
///     as a tourney client of the match via
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.AddTourneyClient" />. A logger
///     correlation scope keyed on the match's database id is opened for the call.
/// </remarks>
public sealed class TourneyMatchJoinChannelHandler(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ILogger<TourneyMatchJoinChannelHandler> logger) : IPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.TournamentJoinMatchChannel;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: tournament channel joins are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the tournament-join-channel packet for the given userSession.</summary>
	/// <param name="userSession">The userSession session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public Task HandleAsync(UserSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();

		if (matchId < 0 || (userSession.Privilege & UserPrivileges.Donator) == 0) return Task.CompletedTask;

		var match = matchRegistry.GetById(matchId);
		if (match is null) return Task.CompletedTask;

		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		if (match.Slots.Any(s => s.PlayerId == userSession.Id))
			return Task.CompletedTask; // already playing in the match

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null && channelMembership.Join(userSession, channel)) match.AddTourneyClient(userSession.Id);

		return Task.CompletedTask;
	}
}