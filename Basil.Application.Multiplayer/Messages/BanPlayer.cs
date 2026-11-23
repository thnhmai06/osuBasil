using Basil.Application.Multiplayer.Objects;
using Basil.Application.Users;
using Basil.Application.Sessions.Objects;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Messages;

public sealed record BanPlayer(Session Actor, Room Room, int PlayerId) : IRequest<RoomTargetResult>;