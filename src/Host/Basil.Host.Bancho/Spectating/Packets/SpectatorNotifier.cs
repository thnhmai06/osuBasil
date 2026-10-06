using Basil.Application.Sessions;
using Basil.Application.Spectating;
using Basil.Protocol.Bancho.Packets;

namespace Basil.Host.Bancho.Spectating.Packets;

/// <summary>
///     Delivers spectator notifications to osu! clients as bancho packets queued on the recipient's
///     session.
/// </summary>
public sealed class SpectatorNotifier : ISpectatorNotifier
{
	public void SpectatorJoined(GameSession host, int spectatorId)
	{
		host.Enqueue(PacketWriter.SpectatorJoined(spectatorId));
	}

	public void FellowSpectatorJoined(GameSession recipient, int fellowId)
	{
		recipient.Enqueue(PacketWriter.FellowSpectatorJoined(fellowId));
	}

	public void FellowSpectatorLeft(GameSession recipient, int fellowId)
	{
		recipient.Enqueue(PacketWriter.FellowSpectatorLeft(fellowId));
	}
}