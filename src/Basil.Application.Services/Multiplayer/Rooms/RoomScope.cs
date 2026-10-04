using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Storage.Multiplayer;

namespace Basil.Application.Services.Multiplayer.Rooms;

/// <summary>Runs state transitions of a room inside its exclusive scope.</summary>
internal static class RoomScope
{
	/// <summary>Runs one state transition of a room inside the room's exclusive scope.</summary>
	/// <param name="room">The room to change.</param>
	/// <param name="operation">The transition to run once the scope is held.</param>
	/// <param name="cancellationToken">A token that cancels the wait for the scope.</param>
	/// <returns>The result of <paramref name="operation" />, or RoomClosed when the room has closed.</returns>
	internal static async Task<RoomResult> InScopeAsync(Room room, Func<RoomResult> operation,
		CancellationToken cancellationToken)
	{
		await using var scope = await Lobby.EnterAsync(room, cancellationToken);
		return scope is null ? RoomResult.RoomClosed : operation();
	}
}
