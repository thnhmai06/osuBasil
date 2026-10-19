using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>Tracks who watches the multiplayer lobby.</summary>
public interface ILobbyWatchersService
{
	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <param name="by">The osu! client that started watching.</param>
	void Watch(BanchoConnection by);

	/// <summary>Stops watching the multiplayer lobby; doing it again does nothing.</summary>
	/// <param name="by">The osu! client that stopped watching.</param>
	void Unwatch(BanchoConnection by);
}
