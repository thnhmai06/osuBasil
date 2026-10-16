using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Multiplayer;

/// <summary>Tracks the clients watching the multiplayer lobby.</summary>
internal sealed class LobbyWatchersService(
	LobbyEventStream events,
	ILobby lobby) : ILobbyWatchersService
{
	/// <inheritdoc />
	public void Watch(BanchoConnection by)
	{
		if (lobby.MutableWatchers.Add(by))
			events.Emit(new LobbyWatcherJoined(by));
	}

	/// <inheritdoc />
	public void Unwatch(BanchoConnection by)
	{
		if (lobby.MutableWatchers.Remove(by))
			events.Emit(new LobbyWatcherLeft(by));
	}
}