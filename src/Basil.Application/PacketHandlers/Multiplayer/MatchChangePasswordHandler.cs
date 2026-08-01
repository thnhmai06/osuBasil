using Basil.Application.PacketHandlers.Core;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Handles the host's request to change the match's password.</summary>
/// <remarks>
///     Only the host may change the password. The new password is read from the client-supplied match
///     snapshot rather than as a standalone field, applied to the match, and the updated state is
///     broadcast to the match channel and the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Application.Sessions.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangePasswordHandler(MatchMembershipService matchMembership) : IBanchoPacketHandler
{
	/// <summary>Gets the client packet this handler processes.</summary>
	public ClientPackets PacketId => ClientPackets.MatchChangePassword;

	/// <summary>
	///     Gets a value that indicates whether the handler may run for restricted players. Always
	///     <see langword="false" />: password changes are not processed for restricted players.
	/// </summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Processes the change-password packet for the given player.</summary>
	/// <param name="player">The player session that sent the packet.</param>
	/// <param name="reader">The packet reader positioned at the payload holding the match snapshot with the new password.</param>
	/// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
	/// <returns>A task that completes when the packet has been handled.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();

		var match = player.Match;
		if (!MatchMembershipService.ValidateMatchData(matchData, player.Id) || match is null ||
		    player.Id != match.HostId) return;

		await match.Lock.WaitAsync();
		try
		{
			match.Password = matchData.Password;
			await matchMembership.EnqueueStateAsync(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}
}