using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Multiplayer.Messages;

public sealed record ListRooms(Session Actor, RoomQuery Query, PageRequest Page) : IRequest<Page<Room>>;