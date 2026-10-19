using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>Changes room membership and records scores submitted during rounds.</summary>
public interface IRoomService : IEventPublisher<RoomEvent>
{
	/// <summary>Joining, leaving, seating, removing and inviting players, and tourney observers.</summary>
	IRoomMembershipService Members { get; }

	/// <summary>Records scores submitted during the room's rounds.</summary>
	IRoomRoundsService Rounds { get; }
}