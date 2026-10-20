using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Announces what happens in the multiplayer lobby.</summary>
internal sealed class LobbyService(LobbyEventStream events) : ILobbyService
{
	/// <inheritdoc />
	public ChannelReader<LobbyEvent> Events => events.Reader;
}
