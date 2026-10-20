using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer.Matches;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>The ports through which the lobby performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating room lifetimes.</param>
/// <param name="Events">The event sink for announcing lobby changes.</param>
/// <param name="Matches">The stored matches.</param>
/// <param name="Settings">The server settings.</param>
/// <param name="RoomEvents">The event sink for room changes.</param>
/// <param name="Chat">The runtime dependencies for room channels.</param>
/// <param name="Users">The user operations needed by rooms.</param>
internal sealed record LobbyContext(
	TimeProvider Clock,
	ILobbyEventSink Events,
	IMatchRepository Matches,
	ISettingsRepository Settings,
	IRoomEventSink RoomEvents,
	ChannelContext Chat,
	IRoomUsersPort Users);

/// <summary>Emits lobby events to the lobby event stream.</summary>
internal interface ILobbyEventSink
{
	/// <summary>Emits a lobby event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(LobbyEvent @event);
}
