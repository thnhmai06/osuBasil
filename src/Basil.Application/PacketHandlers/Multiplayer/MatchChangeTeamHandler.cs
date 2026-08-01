using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's request to switch their team in the match.</summary>
/// <remarks>
///     Toggles the player's slot team between <see cref="Basil.Domain.Multiplayer.MatchTeam.Red" /> and
///     <see cref="Basil.Domain.Multiplayer.MatchTeam.Blue" />, which only matters for team-vs-team match
///     types. The updated state is broadcast to match members but not the lobby, so spectators of the
///     lobby do not see every mid-match team switch. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeTeamHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchChangeTeam;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: team changes are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the change-team packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the start of the payload; this handler does not read the payload.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var match = player.Match;
		if (match is null) return;

		await match.Lock.WaitAsync();
		try
		{
			var slot = match.GetSlot(player.Id);
			if (slot is null) return;

			slot.Team = slot.Team == MatchTeam.Blue ? MatchTeam.Red : MatchTeam.Blue;
			await matchMembership.EnqueueStateAsync(match, false, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}