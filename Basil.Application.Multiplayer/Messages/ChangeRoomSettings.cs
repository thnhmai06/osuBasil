using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks a room to take the settings a client wants; only what differs from the room's own settings changes.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Room">The room to change.</param>
/// <param name="Wanted">The full settings wanted.</param>
public sealed record ChangeRoomSettings(Session Actor, Room Room, WantedRoomSettings Wanted)
	: IRequest<RoomActionResult>;
