using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchInfoRequest.</summary>
public sealed class TourneyMatchInfoRequestHandler(
	IMatchRegistry matchRegistry,
	ILogger<TourneyMatchInfoRequestHandler> logger) : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.TournamentMatchInfoRequest;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();

		if (matchId is < 0 or >= 64 || (player.Privilege & UserPrivileges.Donator) == 0) return Task.CompletedTask;

		var match = matchRegistry.GetById(matchId);
		if (match is null) return Task.CompletedTask;

		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		player.Enqueue(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false));
		return Task.CompletedTask;
	}
}