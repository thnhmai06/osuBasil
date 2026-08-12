using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles a tournament client's request for the current state of a match.</summary>
/// <remarks>
///     Serves only donator-privileged players. The match is looked up in the registry, and an <c>UpdateMatch</c> packet is
///     enqueued for the userSession, built from a read-only snapshot of the match with the password omitted
///     (see <see cref="ServerPacketWriter.UpdateMatch" />). A logger correlation scope keyed on the
///     match's database id is opened for the call. Nothing is mutated, so the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is not taken.
/// </remarks>
public sealed class TourneyMatchInfoRequestHandler(
	IMatchRegistry matchRegistry,
	ILogger<TourneyMatchInfoRequestHandler> logger) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.TournamentMatchInfoRequest;

	public bool AllowedWhenRestricted => false;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchId = reader.ReadI32();

		if (matchId < 0 || (gameSession.Privilege & UserPrivileges.Donator) == 0) return Task.CompletedTask;

		var match = matchRegistry.GetById(matchId);
		if (match is null) return Task.CompletedTask;

		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		gameSession.Enqueue(ServerPacketWriter.UpdateMatch(match.ToPacket(), false));
		return Task.CompletedTask;
	}
}