using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Multiplayer;

/// <summary>Handles the client's request to change the match's mods.</summary>
/// <remarks>
///     Applies the requested mods depending on the match's freemod setting. In a freemod match, the
///     host's packet sets the match-level mods, masked to speed-changing mods only, and each userSession's
///     packet sets that userSession's own slot mods, masked to non-speed-changing mods, so every userSession
///     controls their own modifiers. In a non-freemod match only the host's packet is honored, and it
///     sets the match-level mods directly; other players are ignored. The updated state is broadcast
///     through <see cref="MatchMembershipService.EnqueueStateAsync" />. The read-mutate-broadcast
///     sequence runs under the match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeModsHandler(MatchMembershipService matchMembership) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeMods;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var mods = (Mods)reader.ReadI32();

		var match = gameSession.Match;
		if (match is null) return;

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			if (match.Freemods)
			{
				if (gameSession.Id == match.HostId)
					match.Mods = mods & Mods.SpeedChangingMods;

				var slot = match.GetSlot(gameSession.Id);
				if (slot is null) return;

				slot.Mods = mods & ~Mods.SpeedChangingMods;
			}
			else
			{
				if (gameSession.Id != match.HostId) return;

				match.Mods = mods;
			}

			await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}