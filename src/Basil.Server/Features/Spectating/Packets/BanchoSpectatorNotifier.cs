using Basil.Protocol.Packets;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Spectating.Packets;

/// <summary>
///     Delivers spectator notifications to osu! clients as bancho packets queued on the recipient's
///     session.
/// </summary>
public sealed class BanchoSpectatorNotifier : ISpectatorNotifier
{
	public void SpectatorJoined(GameSession host, int spectatorId)
	{
		host.Enqueue(ServerPacketWriter.SpectatorJoined(spectatorId));
	}

	public void FellowSpectatorJoined(GameSession recipient, int fellowId)
	{
		recipient.Enqueue(ServerPacketWriter.FellowSpectatorJoined(fellowId));
	}

	public void FellowSpectatorLeft(GameSession recipient, int fellowId)
	{
		recipient.Enqueue(ServerPacketWriter.FellowSpectatorLeft(fellowId));
	}
}
