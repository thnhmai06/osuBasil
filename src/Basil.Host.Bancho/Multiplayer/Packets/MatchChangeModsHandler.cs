using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Scores;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Multiplayer.Packets;

/// <summary>Handles the client's request to change the match's mods.</summary>
/// <remarks>
///     Applies the requested mods depending on the match's freemod setting. In a freemod match, the
///     host's packet sets the match-level mods, masked to speed-changing mods only, and each userSession's
///     packet sets that userSession's own slot mods, masked to non-speed-changing mods, so every userSession
///     controls their own modifiers. In a non-freemod match only the host's packet is honored, and it
///     sets the match-level mods directly; other players are ignored. The updated state is broadcast
///     when the mutation scope completes. The read-mutate-broadcast sequence runs under the match's
///     <see cref="MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangeModsHandler(IUserCache userCache) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangeMods;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var mods = (Mods)reader.ReadI32();

		var match = gameSession.Match;
		if (match is null) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		var sender = userCache.Resolve(gameSession);
		if (match.Freemods)
		{
			if (sender.Equals(match.Host))
				match.Mods = mods & Mods.SpeedChangingMods;

			var slot = match.GetSlot(sender);
			if (slot is null) return;

			slot.Mods = mods & ~Mods.SpeedChangingMods;
		}
		else
		{
			if (!sender.Equals(match.Host)) return;

			match.Mods = mods;
		}

		mutation.PublishState();
	}
}