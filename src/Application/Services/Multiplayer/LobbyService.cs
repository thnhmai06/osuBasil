using System.Threading.Channels;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;

namespace Basil.Application.Services.Multiplayer;

/// <summary>Announces what happens in the multiplayer lobby.</summary>
internal sealed class LobbyService(LobbyEventStream events) : ILobbyService
{
	/// <inheritdoc />
	public ChannelReader<LobbyEvent> Events => events.Reader;
}
