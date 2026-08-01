using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles a tournament client's request for the current state of a match.</summary>
/// <remarks>
///     Serves only donator-privileged players and only match ids in the tournament client's id space
///     (0 through 63). The match is looked up in the registry and an <c>UpdateMatch</c> packet is
///     enqueued for the player, built from a read-only snapshot of the match with the password omitted
///     (see <see cref="ServerPacketWriter.UpdateMatch" />). A logger correlation scope keyed on the
///     match's database id is opened for the call. Nothing is mutated, so the match's
///     <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" /> is not taken.
/// </remarks>
public sealed class TourneyMatchInfoRequestHandler(
	IMatchRegistry matchRegistry,
	ILogger<TourneyMatchInfoRequestHandler> logger) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.TournamentMatchInfoRequest;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: tournament info requests are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the tournament-info-request packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match id.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
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