using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Multiplayer.Registry;

internal sealed record LobbyContext(
	TimeProvider Clock,
	IEventPublisher<LobbyEvent> Events,
	IMatchRepository Matches,
	IRoundRepository Rounds,
	IUserRepository UserRepository,
	ISettingsRepository Settings,
	IEventPublisher<RoomEvent> RoomEvents,
	RoomUsers Users);