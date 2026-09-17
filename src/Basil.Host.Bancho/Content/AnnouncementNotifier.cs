using Basil.Application.Content;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Content;

/// <summary>Delivers an announcement to a client as a bancho notification packet.</summary>
public sealed class AnnouncementNotifier : IAnnouncementNotifier
{
	/// <inheritdoc />
	public void Announce(GameSession session, string text)
	{
		session.Enqueue(ServerPacketWriter.Notification(text));
	}
}