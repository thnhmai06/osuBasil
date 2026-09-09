using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Server.Features.Multiplayer.Packets;

/// <summary>Handles the host's request to change the match's password.</summary>
/// <remarks>
///     Only the host may change the password. The new password is read from the client-supplied match
///     snapshot rather than as a standalone field, applied to the match, and the updated state is
///     broadcast to the match channel and the lobby. The read-mutate-broadcast sequence runs under the
///     match's <see cref="Basil.Server.Features.Multiplayer.MatchSession.Lock" />.
/// </remarks>
public sealed class MatchChangePasswordHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.MatchChangePassword;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var matchData = reader.ReadMatch();

		var match = gameSession.Match;
		if (!MatchLifecycle.ValidateMatchData(matchData, gameSession.Id) || match is null ||
		    gameSession.Id != match.HostId) return;

		await using var mutation = await match.BeginMutationAsync(cancellationToken);

		// Re-checked under the lock: host status can only change under this same lock, so a
		// sender who lost host while waiting for it must not still act with host authority.
		if (gameSession.Id != match.HostId) return;

		match.Password = matchData.Password;
		mutation.PublishState();
	}
}