using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchJoinChannel.</summary>
public sealed class TourneyMatchJoinChannelHandler(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ILogger<TourneyMatchJoinChannelHandler> logger) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.TournamentJoinMatchChannel;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();

		if (matchId is < 0 or >= 64 || (player.Privilege & UserPrivileges.Donator) == 0) return Task.CompletedTask;

		var match = matchRegistry.GetById(matchId);
		if (match is null) return Task.CompletedTask;

		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		if (match.Slots.Any(s => s.PlayerId == player.Id)) return Task.CompletedTask; // already playing in the match

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null && channelMembership.Join(player, channel)) match.AddTourneyClient(player.Id);

		return Task.CompletedTask;
	}
}