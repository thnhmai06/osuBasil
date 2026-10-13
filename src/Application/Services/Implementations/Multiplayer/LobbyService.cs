using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Opens and closes rooms and tracks who watches the multiplayer lobby through its children.</summary>
internal sealed class LobbyService(
	LobbyRoomsService rooms,
	LobbyWatchersService watchers,
	LobbyEventStream events) : ILobbyService
{
	/// <inheritdoc />
	public ChannelReader<LobbyEvent> Events => events.Reader;

	/// <inheritdoc />
	public ILobbyRoomsService Rooms => rooms;

	/// <inheritdoc />
	public ILobbyWatchersService Watchers => watchers;
}
