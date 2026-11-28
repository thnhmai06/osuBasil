using Basil.Application.Sessions.Objects;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

/// <summary>Asks for an open room by its id; the answer is <see langword="null"/> when no such room is open or the actor may not view it.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="RoomId">The id of the room.</param>
public sealed record FindRoom(Session Actor, int RoomId) : IRequest<Room?>;
