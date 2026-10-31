using Basil.Application.Common.Events;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Multiplayer.Registry;

/// <summary>The ports through which the lobby performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating room lifetimes.</param>
/// <param name="Events">The publisher for lobby events.</param>
/// <param name="Matches">The stored matches.</param>
/// <param name="Rounds">The stored match rounds.</param>
/// <param name="UserRepository">The stored user records.</param>
/// <param name="Settings">The server settings.</param>
/// <param name="RoomEvents">The publisher for room events.</param>
/// <param name="Users">The questions a room asks about blocks between users.</param>
internal sealed record LobbyContext(
	TimeProvider Clock,
	IEventPublisher<LobbyEvent> Events,
	IMatchRepository Matches,
	IRoundRepository Rounds,
	IUserRepository UserRepository,
	ISettingsRepository Settings,
	IEventPublisher<RoomEvent> RoomEvents,
	RoomBlocks Users);