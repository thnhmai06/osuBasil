using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Multiplayer.Events;
using Basil.Application.Contracts.Multiplayer.Rooms;

namespace Basil.Application.Contracts.Multiplayer;

/// <summary>Changes rooms: who is in them, who runs them, their settings, slots and rounds.</summary>
public interface IRoomService : IEventPublisher<RoomEvent>
{
	/// <summary>Joining, leaving, seating, removing and inviting players, and tourney observers.</summary>
	IRoomMembershipService Members { get; }

	/// <summary>Referees and the host.</summary>
	IRoomAuthorityService Authority { get; }

	/// <summary>The room's shared settings and its lock.</summary>
	IRoomSettingsService Settings { get; }

	/// <summary>Slots and what each player sets on their own slot.</summary>
	IRoomSlotsService Slots { get; }

	/// <summary>Rounds, countdowns and the scores recorded in them.</summary>
	IRoomRoundsService Rounds { get; }
}
