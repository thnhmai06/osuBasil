using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the client's request to change the match's mods.</summary>
/// <remarks>
///     Applies the requested mods depending on the match's freemod setting. In a freemod match, the
///     host's packet sets the match-level mods, masked to speed-changing mods only, and each player's
///     packet sets that player's own slot mods, masked to non-speed-changing mods, so every player
///     controls their own modifiers. In a non-freemod match only the host's packet is honored and it
///     sets the match-level mods directly; other players are ignored. The updated state is broadcast
///     through <see cref="MatchMembershipService.EnqueueStateAsync" />. The read-mutate-broadcast
///     sequence runs under the match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeModsHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchChangeMods;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: mod changes are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the change-mods packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the new mods as an integer.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var mods = (Mods)reader.ReadI32();

		var match = player.Match;
		if (match is null) return;

		await match.Lock.WaitAsync();
		try
		{
			if (match.Freemods)
			{
				if (player.Id == match.HostId) match.Mods = mods & ModsExtensions.SpeedChangingMods;

				var slot = match.GetSlot(player.Id);
				if (slot is null) return;

				slot.Mods = mods & ~ModsExtensions.SpeedChangingMods;
			}
			else
			{
				if (player.Id != match.HostId) return;

				match.Mods = mods;
			}

			await matchMembership.EnqueueStateAsync(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}