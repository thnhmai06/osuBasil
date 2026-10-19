using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>Opens and closes rooms and tracks who watches the multiplayer lobby; rooms and watchers are its parts.</summary>
public interface ILobbyService : IEventPublisher<LobbyEvent>
{
	/// <summary>The open rooms.</summary>
	ILobbyRoomsService Rooms { get; }

	/// <summary>The clients watching the lobby.</summary>
	ILobbyWatchersService Watchers { get; }
}
