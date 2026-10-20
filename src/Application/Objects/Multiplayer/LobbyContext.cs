using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Storage.Content;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Users;

namespace Basil.Application.Objects.Multiplayer;

/// <summary>The ports through which the lobby performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating room lifetimes.</param>
/// <param name="Events">The publisher for lobby events.</param>
/// <param name="Matches">The stored matches.</param>
/// <param name="Rounds">The stored match rounds.</param>
/// <param name="MatchEvents">The stored match events.</param>
/// <param name="UserRepository">The stored user records.</param>
/// <param name="Settings">The server settings.</param>
/// <param name="RoomEvents">The publisher for room events.</param>
/// <param name="Chat">The runtime dependencies for room channels.</param>
/// <param name="Users">The user operations needed by rooms.</param>
internal sealed record LobbyContext(
	TimeProvider Clock,
	IEventPublisher<LobbyEvent> Events,
	IMatchRepository Matches,
	IRoundRepository Rounds,
	IMatchEventRepository MatchEvents,
	IUserRepository UserRepository,
	ISettingsRepository Settings,
	IEventPublisher<RoomEvent> RoomEvents,
	ChannelContext Chat,
	IRoomUsersPort Users);